namespace FalconAuditService;

using System.Collections.Concurrent;
using System.Text.Json;
using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

public class ManifestManager : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ILogger<ManifestManager> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    public ManifestManager(ILogger<ManifestManager> logger) => _logger = logger;

    private SemaphoreSlim LockFor(string manifestPath) =>
        _locks.GetOrAdd(manifestPath, _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Increment the event counter for the current open history entry (async, thread-safe).
    /// Called after each successful InsertAuditEventAsync.
    /// </summary>
    public async Task IncrementEventsAsync(string jobPath)
    {
        var manifestPath = Path.Combine(jobPath, ".audit", "manifest.json");
        var sem = LockFor(manifestPath);
        await sem.WaitAsync();
        try
        {
            var manifest = ReadManifestFile(manifestPath);
            if (manifest is null) return;
            var last = manifest.History.LastOrDefault(e => e.To == null);
            if (last is null) return;
            last.Events++;
            WriteManifest(manifestPath, manifest);
        }
        finally { sem.Release(); }
    }

    /// <summary>
    /// Called when this machine takes ownership of a job folder.
    /// Creates manifest.json if absent; appends a new history entry if the
    /// last entry belongs to a different machine; no-ops if already open for this machine.
    /// </summary>
    public async Task RecordArrivalAsync(string jobPath, string machineName)
    {
        var auditDir     = Path.Combine(jobPath, ".audit");
        var manifestPath = Path.Combine(auditDir, "manifest.json");
        var jobName      = Path.GetFileName(jobPath.TrimEnd('\\', '/'));

        try { Directory.CreateDirectory(auditDir); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ManifestManager: could not create audit dir at {P}.", auditDir);
            return;
        }

        var sem = LockFor(manifestPath);
        await sem.WaitAsync();
        try
        {
            var manifest = ReadManifestFile(manifestPath) ?? new JobManifest
            {
                JobName  = jobName,
                Created  = new MachineTimestamp { Machine = machineName, At = DateTimeOffset.UtcNow }
            };

            var last = manifest.History.LastOrDefault();

            // If last entry is from a different machine and still open, close it
            if (last?.To == null && last is not null && !string.Equals(last.Machine, machineName,
                                                    StringComparison.OrdinalIgnoreCase))
            {
                last.To = DateTimeOffset.UtcNow;
                _logger.LogInformation("ManifestManager: closed entry for {M} on job '{J}'.",
                                        last.Machine, jobName);
            }

            // Open new entry for this machine if needed
            if (last == null || !string.Equals(last.Machine, machineName,
                                                StringComparison.OrdinalIgnoreCase)
                             || last.To != null)
            {
                manifest.History.Add(new HistoryEntry
                {
                    Machine = machineName,
                    From    = DateTimeOffset.UtcNow,
                    To      = null,
                    Events  = 0
                });
                _logger.LogInformation(
                    "ManifestManager: opened entry for {M} on job '{J}'.", machineName, jobName);
            }

            WriteManifest(manifestPath, manifest);
        }
        finally { sem.Release(); }
    }

    /// <summary>
    /// Called when this machine releases ownership (service stop, job folder removed).
    /// Closes the open history entry by setting its 'to' timestamp.
    /// </summary>
    public async Task RecordDepartureAsync(string jobPath)
    {
        var manifestPath = Path.Combine(jobPath, ".audit", "manifest.json");

        var sem = LockFor(manifestPath);
        await sem.WaitAsync();
        try
        {
            var manifest = ReadManifestFile(manifestPath);
            if (manifest is null) return;

            var last = manifest.History.LastOrDefault();
            if (last?.To == null && last is not null)
            {
                last.To = DateTimeOffset.UtcNow;
                WriteManifest(manifestPath, manifest);
                _logger.LogInformation(
                    "ManifestManager: departure recorded for job '{J}'.",
                    Path.GetFileName(jobPath.TrimEnd('\\', '/')));
            }
        }
        finally
        {
            sem.Release();
            // Remove the per-path lock entry so its WaitHandle is released when the job departs.
            _locks.TryRemove(manifestPath, out _);
        }
    }

    /// <summary>Read manifest for a job folder path (public — used by JobOriginChecker).</summary>
    public JobManifest? ReadManifest(string jobPath)
    {
        var manifestPath = Path.Combine(jobPath, ".audit", "manifest.json");
        return ReadManifestFile(manifestPath);
    }

    /// <summary>Persist the detected origin classification into the manifest (async, thread-safe).</summary>
    public async Task UpdateOriginAsync(string jobPath, string origin)
    {
        var manifestPath = Path.Combine(jobPath, ".audit", "manifest.json");
        var sem = LockFor(manifestPath);
        await sem.WaitAsync();
        try
        {
            var manifest = ReadManifestFile(manifestPath);
            if (manifest is null) return;
            manifest.Origin = origin;
            manifest.OriginDeterminedAt = DateTimeOffset.UtcNow;
            WriteManifest(manifestPath, manifest);
        }
        finally { sem.Release(); }
    }

    private JobManifest? ReadManifestFile(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<JobManifest>(
                File.ReadAllText(manifestPath), _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ManifestManager: could not read {P}", manifestPath);
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var sem in _locks.Values) sem.Dispose();
        _locks.Clear();
    }

    private void WriteManifest(string path, JobManifest manifest)
    {
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, _jsonOpts));

            // File.Move(overwrite) is atomic on NTFS only when src and dst are on the same volume.
            if (!string.Equals(Path.GetPathRoot(tmp), Path.GetPathRoot(path),
                                StringComparison.OrdinalIgnoreCase))
                _logger.LogWarning(
                    "ManifestManager: temp and target are on different volumes — " +
                    "manifest write is not atomic. Move job to local NTFS for reliable auditing.");

            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ManifestManager: could not write {P}", path);
        }
    }
}
