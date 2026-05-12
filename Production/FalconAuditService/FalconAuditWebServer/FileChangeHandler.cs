namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

public class FileChangeHandler
{
    private readonly ShardRegistry            _shards;
    private readonly FileClassifier           _classifier;
    private readonly ContentCache             _contentCache;
    private readonly ChangeDescriptionEnricher _enricher;
    private readonly MonitorConfig            _config;
    private readonly LoginReader              _loginReader;
    private readonly ILogger<FileChangeHandler> _logger;

    public FileChangeHandler(
        ShardRegistry shards,
        FileClassifier classifier, ContentCache contentCache,
        ChangeDescriptionEnricher enricher,
        MonitorConfig config, LoginReader loginReader,
        ILogger<FileChangeHandler> logger)
    {
        _shards       = shards;
        _classifier   = classifier;
        _contentCache = contentCache;
        _enricher     = enricher;
        _config       = config;
        _loginReader  = loginReader;
        _logger       = logger;
    }

    internal async Task HandleAsync(ChangeEvent ev)
    {
        _logger.LogDebug("Processing change. Path={P} ChangeType={T}", ev.FullPath, ev.ChangeType);

        // Ignore service-internal files (audit DB writes, manifest writes, etc.)
        if (ev.FullPath.Contains(@"\.audit\", StringComparison.OrdinalIgnoreCase) ||
            ev.FullPath.Contains("/.audit/",  StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping .audit internal file. Path={P}", ev.FullPath);
            return;
        }

        var queue = GetQueue(ev.FullPath);
        if (queue is null)
        {
            _logger.LogDebug("Skipping root-level file (no job). Path={P}", ev.FullPath);
            return;
        }
        var repo = queue.Repository;
        var cls  = _classifier.Classify(ev.FullPath);

        // P4 files are not stored — classifier returns priority "P4" for them.
        if (cls.MonitorPriority == "P4")
        {
            _logger.LogDebug("Skipping P4 file (not stored). Path={P}", ev.FullPath);
            return;
        }

        var baseline = await repo.GetBaselineAsync(ev.FullPath);

        _logger.LogDebug("Classified. Module={M} OwnerService={O} Priority={P}",
                          cls.Module, cls.OwnerService, cls.MonitorPriority);

        string? oldHash    = baseline?.LastHash;
        string? newHash    = null;
        string? oldContent = null;
        string? newContent = null;
        string? diffText   = null;
        string  changeType;

        switch (ev.ChangeType)
        {
            case WatcherChangeTypes.Deleted:
                changeType = "Deleted";
                oldContent = _contentCache.Get(ev.FullPath) ?? baseline?.LastContent;
                break;

            case WatcherChangeTypes.Created:
            case WatcherChangeTypes.Changed:
                if (!File.Exists(ev.FullPath))
                {
                    _logger.LogDebug("File no longer exists, skipping. Path={P}", ev.FullPath);
                    return;
                }
                newHash = HashHelper.ComputeSha256(ev.FullPath);
                if (newHash is null)
                {
                    _logger.LogWarning("Could not hash {P} — skipping.", ev.FullPath);
                    return;
                }
                _logger.LogDebug("Hash computed. OldHash={O} NewHash={N} HashChanged={C}",
                                  oldHash is null ? "null" : oldHash[..Math.Min(8, oldHash.Length)],
                                  newHash[..Math.Min(8, newHash.Length)],
                                  newHash != oldHash);

                if (newHash == oldHash) return;   // no change — baseline unchanged

                changeType = baseline is null ? "Created" : "Modified";

                if (cls.MonitorPriority == "P1" && _config.CaptureContent)
                {
                    var fi = new FileInfo(ev.FullPath);
                    if (fi.Length <= _config.MaxContentBytes)
                    {
                        _logger.LogDebug("Reading content for P1 file. SizeBytes={S}", fi.Length);
                        newContent = await ReadTextAsync(ev.FullPath);
                        oldContent = baseline?.LastContent ?? _contentCache.Get(ev.FullPath);

                        if (changeType == "Modified" && oldContent is not null && newContent is not null)
                        {
                            diffText = DiffHelper.UnifiedDiff(
                                oldContent, newContent, Path.GetFileName(ev.FullPath));
                            _logger.LogDebug("Diff computed. LinesAdded={A} LinesRemoved={R}",
                                              CountDiffLines(diffText, '+'),
                                              CountDiffLines(diffText, '-'));
                        }

                        if (newContent is not null) _contentCache.Set(ev.FullPath, newContent);
                    }
                    else
                    {
                        diffText = $"[content omitted: size {fi.Length:N0} bytes " +
                                    "exceeds max_content_bytes limit]";
                    }
                }
                break;

            case WatcherChangeTypes.Renamed:
                changeType = "Renamed";
                oldContent = baseline?.LastContent ?? _contentCache.Get(ev.OldPath ?? ev.FullPath);
                newHash    = File.Exists(ev.FullPath) ? HashHelper.ComputeSha256(ev.FullPath) : null;
                diffText   = ev.OldPath is not null
                    ? $"{Path.GetFileName(ev.OldPath)} → {Path.GetFileName(ev.FullPath)}"
                    : null;
                if (ev.OldPath is not null)
                {
                    await repo.DeleteBaselineAsync(ev.OldPath);
                    _contentCache.Remove(ev.OldPath);
                }
                break;

            default:
                return;
        }

        var watch = _config.WatchPath.TrimEnd('\\', '/');
        var relFilepath = ev.FullPath.StartsWith(watch, StringComparison.OrdinalIgnoreCase)
            ? ev.FullPath[(watch.Length)..].TrimStart('\\', '/')
            : ev.FullPath;

        var (setup, recipe) = ExtractSetupAndRecipe(ev.FullPath);
        var entry = new AuditLogEntry
        {
            Filepath        = ev.FullPath,
            RelFilepath     = relFilepath,
            EventType       = changeType,
            OldContent      = oldContent,
            DiffText        = diffText,
            Module          = cls.Module,
            OwnerService    = cls.OwnerService,
            MonitorPriority = cls.MonitorPriority,
            ChangedAt       = ev.DetectedAt.ToString("O"),
            MachineName     = _config.MachineName,
            Sha256Hash      = newHash ?? oldHash ?? "",
            FileDescription = cls.Description,
            ChangeSummary   = _enricher.Enrich(cls.MatchedPattern, changeType, diffText),
            OldFilepath     = changeType == "Renamed" ? ev.OldPath : null,
            LoginUser       = _loginReader.GetCurrentUser(),
            Setup           = setup,
            Recipe          = recipe,
            FileEra         = repo.IsInitialScanDone() ? "Runtime" : "JobInit"
        };

        var bl = MakeBaseline(ev.FullPath, newHash ?? oldHash ?? "", newContent ?? oldContent);

        // Lazy-write path: enqueue the event + a manifest bump. The queue
        // flushes to audit.db in batched transactions (per-X-second timer or
        // on cap hit or on a read API call).
        await queue.EnqueueAsync(entry, bl);
        queue.EnqueueManifestBump();

        _logger.LogInformation(
            "Audit event queued. File={F} EventType={C} Module={M} Priority={P}",
            Path.GetFileName(ev.FullPath), changeType, cls.Module, cls.MonitorPriority);

        if (ev.ChangeType == WatcherChangeTypes.Deleted)
        {
            // The baseline is removed in-band so the next event for the same
            // path is treated as a fresh "Created". Note this opens a new
            // SQLite connection on its own — acceptable cost for now.
            await repo.DeleteBaselineAsync(ev.FullPath);
            _contentCache.Remove(ev.FullPath);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private AuditEventQueue? GetQueue(string filePath)
    {
        var (jobName, jobPath) = ExtractJob(filePath);
        if (jobName is null || jobPath is null) return null;
        return _shards.GetOrCreate(jobName, jobPath);
    }

    private (string? jobName, string? jobPath) ExtractJob(string filePath)
    {
        var watch   = _config.WatchPath.TrimEnd('\\', '/');
        if (!filePath.StartsWith(watch, StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var relative = filePath[(watch.Length)..].TrimStart('\\', '/');
        var sep      = relative.IndexOfAny(new[] { '\\', '/' });
        if (sep <= 0) return (null, null);   // direct child of c:\job\ — global file

        var jobName = relative[..sep];
        return (jobName, Path.Combine(watch, jobName));
    }

    // Parses Setup and Recipe from a full file path.
    // Structure: {WatchPath}\{Job}\{Setup}\Recipes\{Recipe}\...
    private (string? setup, string? recipe) ExtractSetupAndRecipe(string filePath)
    {
        var watch = _config.WatchPath.TrimEnd('\\', '/');
        if (!filePath.StartsWith(watch, StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var parts = filePath[(watch.Length)..].TrimStart('\\', '/')
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        // parts[0]=Job  parts[1]=Setup  parts[2]="Recipes"  parts[3]=Recipe
        var setup  = parts.Length > 1 ? parts[1] : null;
        var recipe = parts.Length > 3 &&
                     parts[2].Equals("Recipes", StringComparison.OrdinalIgnoreCase)
                     ? parts[3] : null;
        return (setup, recipe);
    }

    private static FileBaseline MakeBaseline(string path, string hash, string? content) =>
        new()
        {
            Filepath    = path,
            LastHash    = hash,
            LastSeen    = DateTime.UtcNow.ToString("O"),
            LastContent = content
        };

    private static async Task<string?> ReadTextAsync(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open,
                                          FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);
            return await sr.ReadToEndAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int CountDiffLines(string? diff, char prefix) =>
        diff?.Split('\n').Count(l => l.Length > 0 &&
                                     l[0] == prefix &&
                                     (l.Length < 2 || l[1] != prefix)) ?? 0;
}
