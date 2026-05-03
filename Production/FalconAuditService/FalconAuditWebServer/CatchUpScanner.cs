namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

public class CatchUpScanner
{
    private readonly ShardRegistry             _shards;
    private readonly FileClassifier            _classifier;
    private readonly ContentCache              _contentCache;
    private readonly ChangeDescriptionEnricher _enricher;
    private readonly MonitorConfig             _config;
    private readonly ILogger<CatchUpScanner>   _logger;
    private readonly SemaphoreSlim             _guard = new(1, 1);

    private static readonly HashSet<string> IncludedExts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".ini", ".json", ".xml", ".csv", ".log",
            ".yaml", ".yml", ".cfg", ".dat", ".seq", ".md",
            ".properties", ".conf", ".config", ".bat", ".cmd", ".ps1", ".sql"
        };

    public CatchUpScanner(ShardRegistry shards,
                           FileClassifier classifier, ContentCache contentCache,
                           ChangeDescriptionEnricher enricher,
                           MonitorConfig config, ILogger<CatchUpScanner> logger)
    {
        _shards       = shards;
        _classifier   = classifier;
        _contentCache = contentCache;
        _enricher     = enricher;
        _config       = config;
        _logger       = logger;
    }

    /// <summary>
    /// Run catch-up scans for all jobs in parallel.
    /// Each job runs in its own Task; per-shard SemaphoreSlim(1) serialises writes.
    /// </summary>
    public async Task RunAllJobsParallelAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_config.WatchPath))
        {
            _logger.LogWarning("CatchUpScanner: watch path does not exist: {P}", _config.WatchPath);
            return;
        }

        var jobNames = Directory.EnumerateDirectories(_config.WatchPath)
                                .Select(Path.GetFileName)
                                .Where(n => !string.IsNullOrEmpty(n))
                                .Cast<string>()
                                .ToList();

        var tasks = jobNames.Select(async jn =>
        {
            var jp = Path.Combine(_config.WatchPath, jn);
            try { await RunJobAsync(jn, jp, ct); }
            catch (Exception ex) { _logger.LogError(ex, "CatchUp failed for {Job}", jn); }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Reconcile disk state against stored baselines for a single job.
    /// </summary>
    public async Task RunJobAsync(string jobName, string jobPath, CancellationToken ct)
    {
        var repo = _shards.GetOrCreate(jobName, jobPath);
        if (repo is null)
        {
            _logger.LogWarning("CatchUpScanner: skipping {Job} — shard could not be opened.", jobName);
            return;
        }
        await CoreAsync(_config.WatchPath, ct, jobPath);
    }

    /// <summary>
    /// Reconcile disk state against stored baselines.
    /// Pass jobPath to restrict scan to one job subtree;
    /// pass null for a full scan of the entire watch path (used on full restart).
    /// </summary>
    public async Task RunAsync(string watchPath, CancellationToken ct, string? jobPath = null)
    {
        if (!await _guard.WaitAsync(0))
        {
            _logger.LogWarning("CatchUpScanner: already running — skipping.");
            return;
        }
        try   { await CoreAsync(watchPath, ct, jobPath); }
        finally { _guard.Release(); }
    }

    private async Task CoreAsync(string watchPath, CancellationToken ct, string? jobPath)
    {
        var scanRoot = jobPath ?? watchPath;
        if (!Directory.Exists(scanRoot))
        {
            _logger.LogWarning("CatchUpScanner: scan root does not exist: {R}", scanRoot);
            return;
        }
        var sw       = System.Diagnostics.Stopwatch.StartNew();

        // Per-job era cache: true = first ever scan of this shard (→ "JobInit"), false → "Runtime".
        var jobInitFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        string EraForFile(string filePath)
        {
            var watch    = _config.WatchPath.TrimEnd('\\', '/');
            var relative = filePath.StartsWith(watch, StringComparison.OrdinalIgnoreCase)
                           ? filePath[(watch.Length)..].TrimStart('\\', '/') : filePath;
            var sep      = relative.IndexOfAny(new[] { '\\', '/' });
            if (sep <= 0) return "Runtime";
            var jobName  = relative[..sep];
            if (jobInitFlags.TryGetValue(jobName, out var cached)) return cached ? "JobInit" : "Runtime";
            var jPath    = Path.Combine(watch, jobName);
            var repo     = _shards.GetOrCreate(jobName, jPath);
            bool isInit  = repo is not null && !repo.IsInitialScanDone();
            jobInitFlags[jobName] = isInit;
            return isInit ? "JobInit" : "Runtime";
        }

        _logger.LogInformation("CatchUpScanner: starting reconciliation scan. Root={R}", scanRoot);

        var currentFiles = Directory
            .EnumerateFiles(scanRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => IncludedExts.Contains(Path.GetExtension(f)))
            .Where(f => !f.Contains(@"\.audit\", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains("/.audit/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation("CatchUpScanner: found {N} candidate files.", currentFiles.Count);

        // Build a per-repo baseline map: only load baselines from repos we'll scan
        // For a scoped job scan, get baselines only from that job's shard.
        List<FileBaseline> allBaselines;
        if (jobPath is not null)
        {
            // Scoped scan: pull baselines from this job's shard only.
            var jobName = Path.GetFileName(jobPath.TrimEnd('\\', '/'));
            var repo    = _shards.GetOrCreate(jobName, jobPath);
            allBaselines = repo is not null
                ? await repo.GetAllBaselinesAsync()
                : new List<FileBaseline>();
        }
        else
        {
            allBaselines = await GetAllBaselinesAsync(currentFiles);
        }

        var baselineMap  = allBaselines
            .GroupBy(b => b.Filepath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var currentSet   = new HashSet<string>(currentFiles, StringComparer.OrdinalIgnoreCase);

        int created = 0, modified = 0, deleted = 0, unchanged = 0;

        // ── Phase 1: inspect current files ──────────────────────────────────
        foreach (var path in currentFiles)
        {
            ct.ThrowIfCancellationRequested();

            string? hash; long size;
            try { hash = HashHelper.ComputeSha256(path); size = new FileInfo(path).Length; }
            catch (IOException) { continue; }
            if (hash is null) continue;

            var cls    = _classifier.Classify(path);
            if (cls.MonitorPriority == "P4") continue;  // not stored

            var fileRepo = GetRepo(path);
            if (fileRepo is null) continue;
            baselineMap.TryGetValue(path, out var bl);

            var rel = MakeRelPath(path);
            if (bl is null)
            {
                string? content = await ReadIfP1Async(path, cls.MonitorPriority, size);
                if (content is not null) _contentCache.Set(path, content);

                var entry = new AuditLogEntry
                {
                    Filepath        = path,
                    RelFilepath     = rel,
                    EventType       = "Created",
                    Sha256Hash      = hash,
                    OldContent      = content,
                    Module          = cls.Module,
                    OwnerService    = cls.OwnerService,
                    MonitorPriority = cls.MonitorPriority,
                    ChangedAt       = DateTime.UtcNow.ToString("O"),
                    MachineName     = _config.MachineName,
                    FileDescription = cls.Description,
                    ChangeSummary   = _enricher.Enrich(cls.MatchedPattern, "Created", null),
                    IsBackfill      = true,
                    FileEra         = EraForFile(path)
                };
                var baseline = new FileBaseline { Filepath = path, LastHash = hash,
                    LastSeen = DateTime.UtcNow.ToString("O"), LastContent = content };
                await fileRepo.InsertAuditEventAsync(entry, baseline);
                created++;
            }
            else if (hash != bl.LastHash)
            {
                string? newContent = await ReadIfP1Async(path, cls.MonitorPriority, size);
                if (newContent is not null) _contentCache.Set(path, newContent);

                string? diffText = null;
                if (cls.MonitorPriority == "P1" && bl.LastContent is not null && newContent is not null)
                    diffText = DiffHelper.UnifiedDiff(bl.LastContent, newContent, Path.GetFileName(path));

                var entry = new AuditLogEntry
                {
                    Filepath        = path,
                    RelFilepath     = rel,
                    EventType       = "Modified",
                    Sha256Hash      = hash,
                    OldContent      = bl.LastContent,
                    DiffText        = diffText,
                    Module          = cls.Module,
                    OwnerService    = cls.OwnerService,
                    MonitorPriority = cls.MonitorPriority,
                    ChangedAt       = DateTime.UtcNow.ToString("O"),
                    MachineName     = _config.MachineName,
                    FileDescription = cls.Description,
                    ChangeSummary   = _enricher.Enrich(cls.MatchedPattern, "Modified", diffText),
                    IsBackfill      = true,
                    FileEra         = EraForFile(path)
                };
                var baseline = new FileBaseline { Filepath = path, LastHash = hash,
                    LastSeen = DateTime.UtcNow.ToString("O"), LastContent = newContent };
                await fileRepo.InsertAuditEventAsync(entry, baseline);
                modified++;
            }
            else
            {
                if (cls.MonitorPriority == "P1" && _config.CaptureContent &&
                    size <= _config.MaxContentBytes)
                {
                    var content = await ReadIfP1Async(path, cls.MonitorPriority, size);
                    if (content is not null) _contentCache.Set(path, content);
                }
                // No audit event for unchanged files — just refresh LastSeen
                await fileRepo.UpsertBaselineAsync(new FileBaseline
                {
                    Filepath    = path,
                    LastHash    = hash,
                    LastSeen    = DateTime.UtcNow.ToString("O"),
                    LastContent = bl.LastContent
                });
                unchanged++;
            }
        }

        // ── Phase 2: detect deletions ────────────────────────────────────────
        foreach (var bl in allBaselines)
        {
            ct.ThrowIfCancellationRequested();
            if (currentSet.Contains(bl.Filepath)) continue;

            var fileRepo = GetRepo(bl.Filepath);
            if (fileRepo is null) continue;
            var cls2     = _classifier.Classify(bl.Filepath);
            if (cls2.MonitorPriority == "P4") continue;
            var entry2 = new AuditLogEntry
            {
                Filepath        = bl.Filepath,
                RelFilepath     = MakeRelPath(bl.Filepath),
                EventType       = "Deleted",
                Sha256Hash      = bl.LastHash,
                OldContent      = bl.LastContent,
                Module          = cls2.Module,
                OwnerService    = cls2.OwnerService,
                MonitorPriority = cls2.MonitorPriority,
                ChangedAt       = DateTime.UtcNow.ToString("O"),
                MachineName     = _config.MachineName,
                FileDescription = cls2.Description,
                ChangeSummary   = _enricher.Enrich(cls2.MatchedPattern, "Deleted", null),
                IsBackfill      = true,
                FileEra         = EraForFile(bl.Filepath)
            };
            var dummyBaseline = new FileBaseline
            {
                Filepath = bl.Filepath, LastHash = bl.LastHash, LastSeen = bl.LastSeen
            };
            await fileRepo.InsertAuditEventAsync(entry2, dummyBaseline);
            await fileRepo.DeleteBaselineAsync(bl.Filepath);
            _contentCache.Remove(bl.Filepath);
            deleted++;
        }

        // Persist initial_scan_done for every job that just ran its first-ever scan.
        foreach (var (jn, wasInit) in jobInitFlags)
        {
            if (!wasInit) continue;
            var jp   = Path.Combine(_config.WatchPath.TrimEnd('\\', '/'), jn);
            var repo = _shards.GetOrCreate(jn, jp);
            if (repo is not null) await repo.SetInitialScanDoneAsync();
        }

        sw.Stop();
        _logger.LogInformation(
            "CatchUpScanner: complete. Unchanged={U} Created={C} Modified={M} Deleted={D} Elapsed={E}ms",
            unchanged, created, modified, deleted, sw.ElapsedMilliseconds);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private SqliteRepository? GetRepo(string filePath)
    {
        var watch = _config.WatchPath.TrimEnd('\\', '/');
        if (!filePath.StartsWith(watch, StringComparison.OrdinalIgnoreCase))
            return null;

        var relative = filePath[(watch.Length)..].TrimStart('\\', '/');
        var sep      = relative.IndexOfAny(new[] { '\\', '/' });
        if (sep <= 0) return null;

        var jobName = relative[..sep];
        var jobPath = Path.Combine(watch, jobName);
        return _shards.GetOrCreate(jobName, jobPath);
    }

    private string MakeRelPath(string filePath)
    {
        var watch = _config.WatchPath.TrimEnd('\\', '/');
        return filePath.StartsWith(watch, StringComparison.OrdinalIgnoreCase)
            ? filePath[(watch.Length)..].TrimStart('\\', '/')
            : filePath;
    }

    private async Task<List<FileBaseline>> GetAllBaselinesAsync(List<string> currentFiles)
    {
        var result   = new List<FileBaseline>();
        var jobNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var watch    = _config.WatchPath.TrimEnd('\\', '/');

        foreach (var f in currentFiles)
        {
            if (!f.StartsWith(watch, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = f[(watch.Length)..].TrimStart('\\', '/');
            var sep = rel.IndexOfAny(new[] { '\\', '/' });
            if (sep > 0) jobNames.Add(rel[..sep]);
        }

        foreach (var jn in jobNames)
        {
            var jp   = Path.Combine(_config.WatchPath, jn);
            var repo = _shards.GetOrCreate(jn, jp);
            if (repo is null) continue;
            result.AddRange(await repo.GetAllBaselinesAsync());
        }
        return result;
    }

    private async Task<string?> ReadIfP1Async(string path, string priority, long size)
    {
        if (priority != "P1" || !_config.CaptureContent) return null;
        if (size > _config.MaxContentBytes) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open,
                                          FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);
            return await sr.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReadIfP1Async: could not read {P}", path);
            return null;
        }
    }
}
