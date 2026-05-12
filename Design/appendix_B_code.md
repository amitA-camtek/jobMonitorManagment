# Appendix B — Complete Source Code

> **Belongs to:** `jobMonitorManagmentDesign.md`  
> **Design option implemented:** Option C — Job-Embedded Shard with Custody Manifest  
> **Base:** `04_recommended_design.md` Appendix A, modified for per-job shard architecture  
> **Target:** .NET 8, C# 12, `net8.0-windows` — single merged exe (`FalconAuditService.exe`) hosting both the audit `BackgroundService` worker and the Kestrel REST API on port 5100

Files marked **[NEW]**, **[MODIFIED]**, or **[UNCHANGED]**.

---

## B.1 — `FalconAuditWebServer.csproj` [MODIFIED]

Changes: base SDK changed from `Microsoft.NET.Sdk.Worker` to `Microsoft.NET.Sdk.Web`; target framework upgraded to `net8.0-windows`; added `Microsoft.AspNetCore.Authentication.Negotiate`, `Microsoft.Extensions.Hosting.WindowsServices`, `Serilog.AspNetCore`, `Serilog.Settings.Configuration`; assembly name stays `FalconAuditService` so the deployed exe filename is unchanged.

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <AssemblyName>FalconAuditService</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite"                          Version="8.0.*" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Negotiate" Version="8.0.*" />
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices"  Version="8.0.*" />
    <PackageReference Include="DiffPlex"                                       Version="1.7.*" />
    <PackageReference Include="Serilog.AspNetCore"                            Version="8.0.*" />
    <PackageReference Include="Serilog.Settings.Configuration"                Version="8.0.*" />
    <PackageReference Include="Serilog.Sinks.File"                            Version="5.0.*" />
    <PackageReference Include="Serilog.Sinks.EventLog"                        Version="3.1.*" />
  </ItemGroup>
  <ItemGroup>
    <Content Update="FileClassificationRules.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Update="ParameterDescriptions.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

---

## B.2 — `appsettings.json` [MODIFIED]

Changes: `Kestrel` section added for REST API binding on port 5100; `AuditService` section keys aligned with merged service.

> **Config source of truth:** Operational config (debounce_ms, capture_content, max_content_bytes, etc.) is stored in the `monitor_config` SQLite table loaded by `SqliteRepository.LoadConfig()`. The `appsettings.json` file contains only path overrides that make sense as environment-level settings — these override the SQL values when present.

```json
{
  "AuditService": {
    "GlobalDbPath":              "C:\\bis\\auditlog\\global.db",
    "ClassificationRulesPath":  "C:\\bis\\auditlog\\FileClassificationRules.json",
    "ParameterDescriptionsPath":"C:\\bis\\auditlog\\ParameterDescriptions.json"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5100" }
    }
  },
  "Serilog": {
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "C:\\bis\\auditlog\\logs\\falconaudit-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 31
        }
      },
      {
        "Name": "EventLog",
        "Args": { "source": "FalconAuditService", "restrictedToMinimumLevel": "Warning" }
      }
    ]
  }
}
```

---

## B.3 — `Models/AuditLogEntry.cs` [MODIFIED]

Changes: `FileDescription` and `ChangeSummary` added to carry user-friendly context from `ChangeDescriptionEnricher`.

```csharp
namespace FalconAuditService.Models;

public record AuditLogEntry
{
    public string  Filepath         { get; init; } = "";
    public string  RelFilepath      { get; init; } = "";   // path relative to job folder
    public string  EventType        { get; init; } = "";   // Created|Modified|Deleted|Renamed
    public string? OldContent       { get; init; }         // full text before  (P1 only)
    public string? DiffText         { get; init; }         // unified diff      (P1 Modified only)
    public string  Module           { get; init; } = "Unknown";
    public string  OwnerService     { get; init; } = "Unknown";
    public string  MonitorPriority  { get; init; } = "P3";
    public string  ChangedAt        { get; init; } = "";   // ISO-8601 UTC
    public string  MachineName      { get; init; } = "";
    public string  Sha256Hash       { get; init; } = "";
    public string  FileDescription  { get; init; } = "";   // human-readable file purpose
    public string  ChangeSummary    { get; init; } = "";   // human-readable change summary
    public bool    IsBackfill       { get; init; } = false; // true when written by CatchUpScanner
    public string? OldFilepath      { get; init; }          // populated for Renamed events only
}
```

---

## B.4 — `Models/FileBaseline.cs` [UNCHANGED]

```csharp
namespace FalconAuditService.Models;

public record FileBaseline
{
    public string  Filepath     { get; init; } = "";
    public string  LastHash     { get; init; } = "";
    public string  LastSeen     { get; init; } = "";   // ISO-8601 UTC
    public string? LastContent  { get; init; }         // cached content for P1 diff (may be null)
}
```

---

## B.5 — `Models/MonitorConfig.cs` [MODIFIED]

Changes: `DbPath` → `GlobalDbPath`; `ClassificationRulesPath` and `ParameterDescriptionsPath` added.

```csharp
namespace FalconAuditService.Models;

public class MonitorConfig
{
    public string WatchPath                  { get; set; } = @"C:\job\";
    public string GlobalDbPath               { get; set; } = @"C:\bis\auditlog\global.db";
    public string ClassificationRulesPath    { get; set; } = @"C:\bis\auditlog\FileClassificationRules.json";
    public string ParameterDescriptionsPath  { get; set; } = @"C:\bis\auditlog\ParameterDescriptions.json";
    public int    ApiPort                    { get; set; } = 5100;
    public string ApiBindAddress             { get; set; } = "127.0.0.1";
    public int    DebounceMs                 { get; set; } = 500;
    public int    FswBufferBytes             { get; set; } = 65_536;
    public long   MaxContentBytes            { get; set; } = 1_048_576;
    public bool   CaptureContent             { get; set; } = true;
    public int    CatchUpYieldThreshold      { get; set; } = 50;
    public int    RecoveryDelayMs            { get; set; } = 30_000;  // delay before full re-hash after FSW overflow
    public string MachineName                { get; set; } = Environment.MachineName;   // REC-006 (#26)
}
```

---

## B.6 — `Models/JobManifest.cs` [NEW]

```csharp
namespace FalconAuditService.Models;

using System.Text.Json.Serialization;

public class JobManifest
{
    [JsonPropertyName("jobName")]
    public string JobName { get; set; } = "";

    [JsonPropertyName("auditDbVersion")]
    public string AuditDbVersion { get; set; } = "1";

    [JsonPropertyName("created")]
    public MachineTimestamp? Created { get; set; }

    [JsonPropertyName("history")]
    public List<HistoryEntry> History { get; set; } = new();
}

public class MachineTimestamp
{
    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("at")]
    public DateTime At { get; set; }
}

public class HistoryEntry
{
    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("from")]
    public DateTime From { get; set; }

    [JsonPropertyName("to")]
    public DateTime? To { get; set; }

    [JsonPropertyName("events")]
    public int Events { get; set; }
}
```

---

## B.7 — `ContentCache.cs` [MODIFIED]

Changes: replaced unbounded `ConcurrentDictionary` with a size-bounded LRU cache to prevent unbounded memory growth on long-running services with hundreds of large recipes.

```csharp
namespace FalconAuditService;

using System.Collections.Generic;

/// <summary>
/// Thread-safe LRU cache for P1 file content. Evicts oldest entries when the
/// total byte estimate exceeds MaxBytes. Each char is counted as 2 bytes.
/// </summary>
public class ContentCache
{
    private readonly long _maxBytes;
    private long _totalBytes;
    private readonly Dictionary<string, LinkedListNode<(string key, string value)>> _map =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string key, string value)> _order = new();
    private readonly object _lock = new();

    public ContentCache(long maxBytes = 200L * 1024 * 1024)   // 200 MB default
    {
        _maxBytes = maxBytes;
    }

    public void Set(string path, string content)
    {
        long newBytes = content.Length * 2L;
        lock (_lock)
        {
            if (_map.TryGetValue(path, out var existing))
            {
                _totalBytes -= existing.Value.value.Length * 2L;
                _order.Remove(existing);
                _map.Remove(path);
            }

            while (_totalBytes + newBytes > _maxBytes && _order.Count > 0)
            {
                var oldest = _order.First!;
                _totalBytes -= oldest.Value.value.Length * 2L;
                _map.Remove(oldest.Value.key);
                _order.RemoveFirst();
            }

            var node = _order.AddLast((path, content));
            _map[path] = node;
            _totalBytes += newBytes;
        }
    }

    public string? Get(string path)
    {
        lock (_lock)
            return _map.TryGetValue(path, out var node) ? node.Value.value : null;
    }

    public void Remove(string path)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(path, out var node)) return;
            _totalBytes -= node.Value.value.Length * 2L;
            _order.Remove(node);
            _map.Remove(path);
        }
    }
}
```

---

## B.8 — `HashHelper.cs` [UNCHANGED]

```csharp
namespace FalconAuditService;

using System.Security.Cryptography;

public static class HashHelper
{
    private const int MaxRetries   = 3;
    private const int RetryDelayMs = 100;

    public static string? ComputeSha256(string path)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var fs   = new FileStream(path, FileMode.Open,
                                                FileAccess.Read, FileShare.ReadWrite);
                using var sha  = SHA256.Create();
                byte[]    hash = sha.ComputeHash(fs);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                Thread.Sleep(RetryDelayMs * (attempt + 1));
            }
            catch (Exception)
            {
                return null;
            }
        }
        return null;
    }
}
```

---

## B.9 — `DiffHelper.cs` [UNCHANGED]

```csharp
namespace FalconAuditService;

using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

public static class DiffHelper
{
    private const int ContextLines = 3;

    public static string? UnifiedDiff(
        string?  oldText,
        string?  newText,
        string   fileName,
        DateTime oldTime = default,
        DateTime newTime = default)
    {
        if (oldText is null || newText is null) return null;

        var diff = InlineDiffBuilder.Diff(oldText, newText);
        if (!diff.HasDifferences) return null;

        var lines = diff.Lines;
        int n     = lines.Count;

        var inHunk = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (lines[i].Type == ChangeType.Unchanged) continue;
            for (int j = Math.Max(0, i - ContextLines);
                     j < Math.Min(n, i + ContextLines + 1); j++)
                inHunk[j] = true;
        }

        var sb    = new StringBuilder();
        var oldTs = oldTime == default ? "" : $"  {oldTime:O}";
        var newTs = newTime == default ? "" : $"  {newTime:O}";
        sb.AppendLine($"--- {fileName}{oldTs} (before)");
        sb.AppendLine($"+++ {fileName}{newTs} (after)");

        int oldNo = 1, newNo = 1, i2 = 0;

        while (i2 < n)
        {
            if (!inHunk[i2])
            {
                if (lines[i2].Type != ChangeType.Inserted) oldNo++;
                if (lines[i2].Type != ChangeType.Deleted)  newNo++;
                i2++;
                continue;
            }

            int start = i2;
            while (i2 < n && inHunk[i2]) i2++;
            int end = i2;

            var hunk   = lines.GetRange(start, end - start);
            int oldCnt = hunk.Count(l => l.Type != ChangeType.Inserted);
            int newCnt = hunk.Count(l => l.Type != ChangeType.Deleted);

            sb.AppendLine($"@@ -{oldNo},{oldCnt} +{newNo},{newCnt} @@");

            foreach (var line in hunk)
            {
                char pfx = line.Type switch
                {
                    ChangeType.Inserted => '+',
                    ChangeType.Deleted  => '-',
                    _                   => ' '
                };
                sb.AppendLine($"{pfx}{line.Text}");
                if (line.Type != ChangeType.Inserted) oldNo++;
                if (line.Type != ChangeType.Deleted)  newNo++;
            }
        }

        return sb.ToString().TrimEnd();
    }
}
```

---

## B.10 — `FileClassifier.cs` [MODIFIED]

Changes: rules loaded from `FileClassificationRules.json` via `LoadRules()`. Hot-reload via a secondary `FileSystemWatcher`. `ImmutableList` swap is lock-free on the read path. `ClassificationResult` extended with `ShortName`, `Description`, and `MatchedPattern` to support user-friendly reporting.

```csharp
namespace FalconAuditService;

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

public class FileClassifier : IDisposable
{
    public record ClassificationResult(
        string Module,           // Job|Recipe|Config|AlignmentData|DieMap|ScanResult|Log|Unknown
        string OwnerService,     // RMS|Falcon.Net|AOI_Main|DataServer|Unknown
        string MonitorPriority,  // P1|P2|P3|P4
        string MatchedPattern,   // raw pattern string from rules file (used as ParameterDescriptions key)
        string ShortName,        // human-readable file name (e.g. "Recipe auto-cycle settings")
        string Description       // human-readable file purpose (one sentence)
    );

    private record CompiledRule(Regex Regex, string RawPattern, ClassificationResult Result);

    private ImmutableList<CompiledRule>      _rules   = ImmutableList<CompiledRule>.Empty;
    private ClassificationResult             _default = new("Unknown", "Unknown", "P3", "", "Unknown file", "Unclassified file change.");
    private FileSystemWatcher?               _configWatcher;
    private Timer?                           _reloadDebounce;
    private readonly ILogger<FileClassifier> _logger;

    public FileClassifier(ILogger<FileClassifier> logger) => _logger = logger;

    // ── Load / Hot-reload ────────────────────────────────────────────────────

    public void LoadRules(string configPath)
    {
        try
        {
            var json    = File.ReadAllText(configPath);
            var ruleset = JsonSerializer.Deserialize<RuleSet>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                                            ReadCommentHandling = JsonCommentHandling.Skip });

            if (ruleset?.Rules is null)
            {
                _logger.LogWarning("FileClassificationRules.json has no rules.");
                return;
            }

            var compiled = ruleset.Rules
                .Select(r =>
                {
                    var normPattern = r.Pattern.ToLowerInvariant().Replace('\\', '/');
                    var result = new ClassificationResult(
                        r.Module, r.OwnerService, r.MonitorPriority,
                        r.Pattern,
                        r.ShortName   ?? r.Module,
                        r.Description ?? "");
                    return new CompiledRule(GlobToRegex(normPattern), r.Pattern, result);
                })
                .ToImmutableList();

            // Atomic publication — Classify() readers get either old or new list, never torn.
            Interlocked.Exchange(ref _rules, compiled);

            if (ruleset.DefaultClassification is not null)
                _default = new ClassificationResult(
                    ruleset.DefaultClassification.Module,
                    ruleset.DefaultClassification.OwnerService,
                    ruleset.DefaultClassification.MonitorPriority,
                    "",
                    ruleset.DefaultClassification.ShortName   ?? "Unknown file",
                    ruleset.DefaultClassification.Description ?? "Unclassified file change.");
            else  // reset to fallback if default block removed from config
                _default = new ClassificationResult("Unknown", "Unknown", "P3", "", "Unknown file", "Unclassified file change.");

            _logger.LogInformation("FileClassifier: loaded {N} rules from {P}",
                compiled.Count, configPath);

            StartConfigWatcher(configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileClassifier: failed to load rules from {P}", configPath);
        }
    }

    private void StartConfigWatcher(string configPath)
    {
        if (_configWatcher is not null) return;   // already watching

        var dir  = Path.GetDirectoryName(configPath)!;
        var file = Path.GetFileName(configPath);

        _configWatcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilters       = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _configWatcher.Changed += (_, _) =>
        {
            // Debounce: JSON file may still be partially written
            _reloadDebounce?.Dispose();
            _reloadDebounce = new Timer(_ => LoadRules(configPath), null, 1000, Timeout.Infinite);
        };
        _logger.LogInformation("FileClassifier: watching {P} for hot-reload.", configPath);
    }

    // ── Classify ─────────────────────────────────────────────────────────────

    public ClassificationResult Classify(string filePath)
    {
        var norm  = filePath.ToLowerInvariant().Replace('\\', '/');
        var rules = _rules;   // snapshot — lock-free

        foreach (var rule in rules)
            if (rule.Regex.IsMatch(norm)) return rule.Result;

        return _default;
    }

    // ── Glob → Regex ─────────────────────────────────────────────────────────

    private static Regex GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder("^");
        int i  = 0;
        while (i < glob.Length)
        {
            if (glob[i] == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                sb.Append(".*");
                i += 2;
                if (i < glob.Length && glob[i] == '/') i++;
            }
            else if (glob[i] == '*') { sb.Append("[^/]*"); i++; }
            else if (glob[i] == '?') { sb.Append("[^/]");  i++; }
            else if (glob[i] == '.') { sb.Append("\\.");   i++; }
            else { sb.Append(Regex.Escape(glob[i].ToString())); i++; }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    // ── JSON schema types ────────────────────────────────────────────────────

    private record RuleEntry(string Pattern, string MatchType,
                              string Module, string OwnerService, string MonitorPriority,
                              string? ShortName, string? Description);
    private record DefaultEntry(string Module, string OwnerService, string MonitorPriority,
                                string? ShortName, string? Description);
    private record RuleSet(List<RuleEntry>? Rules, DefaultEntry? DefaultClassification);

    public void Dispose()
    {
        _configWatcher?.Dispose();
        _reloadDebounce?.Dispose();
    }
}
```

---

## B.10b — `ChangeDescriptionEnricher.cs` [NEW]

Loads `ParameterDescriptions.json` and produces a human-readable `changeSummary` from a unified diff and a file's `MatchedPattern`. Hot-reloads via a `FileSystemWatcher`.

**Algorithm:**
1. Look up `MatchedPattern` in the descriptions map.
2. Parse the diff for `[Section]` headers in context lines, and extract `-key=old` / `+key=new` pairs.
3. Map each changed key to its human label (fall back to the raw key name).
4. Format: `"Label: old → new"` per parameter; join with `"; "`.

```csharp
namespace FalconAuditService;

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

public class ChangeDescriptionEnricher : IDisposable
{
    // pattern (as stored in ClassificationResult.MatchedPattern) →
    //   (section.key, lowercased) → human label
    private ImmutableDictionary<string, ImmutableDictionary<string, string>> _map =
        ImmutableDictionary<string, ImmutableDictionary<string, string>>.Empty;

    private FileSystemWatcher?                    _watcher;
    private Timer?                                _debounce;
    private readonly ILogger<ChangeDescriptionEnricher> _logger;

    private static readonly Regex _sectionRx  = new(@"^\s*\[([^\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex _keyValueRx  = new(@"^([+\- ])\s*([^=\s][^=]*)=(.*)$", RegexOptions.Compiled);

    public ChangeDescriptionEnricher(ILogger<ChangeDescriptionEnricher> logger) => _logger = logger;

    // ── Load / Hot-reload ────────────────────────────────────────────────────

    public void Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            _logger.LogWarning("ChangeDescriptionEnricher: {P} not found — change summaries will use raw key names.", configPath);
            StartWatcher(configPath);
            return;
        }
        try
        {
            var json = File.ReadAllText(configPath);
            var doc  = JsonSerializer.Deserialize<DescriptionsFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                                            ReadCommentHandling = JsonCommentHandling.Skip });

            if (doc?.Files is null) { _logger.LogWarning("ChangeDescriptionEnricher: no files map in {P}.", configPath); return; }

            var builder = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var (pattern, keys) in doc.Files)
            {
                var inner = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in keys) inner[k] = v;
                builder[pattern] = inner.ToImmutable();
            }

            Interlocked.Exchange(ref _map, builder.ToImmutable());
            _logger.LogInformation("ChangeDescriptionEnricher: loaded {N} file patterns from {P}.",
                doc.Files.Count, configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChangeDescriptionEnricher: failed to load {P}.", configPath);
        }
        finally { StartWatcher(configPath); }
    }

    private void StartWatcher(string configPath)
    {
        if (_watcher is not null) return;
        var dir  = Path.GetDirectoryName(configPath);
        if (dir is null || !Directory.Exists(dir)) return;
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(configPath))
        {
            NotifyFilters = NotifyFilters.LastWrite, EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) =>
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => Load(configPath), null, 1000, Timeout.Infinite);
        };
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Produce a human-readable change summary for one audit event.
    /// </summary>
    /// <param name="matchedPattern">ClassificationResult.MatchedPattern for this file.</param>
    /// <param name="eventType">Created | Modified | Deleted | Renamed</param>
    /// <param name="diffText">Unified diff text (may be null for non-P1 events).</param>
    public string Enrich(string matchedPattern, string eventType, string? diffText)
    {
        if (eventType == "Created")  return "File created";
        if (eventType == "Deleted")  return $"File deleted: {Path.GetFileName(matchedPattern)}";
        if (eventType == "Renamed")  return !string.IsNullOrEmpty(diffText)
            ? $"File renamed: {diffText}"
            : "File renamed";
        if (string.IsNullOrEmpty(diffText)) return "File modified";

        var changes = ParseDiffChanges(diffText);
        if (changes.Count == 0) return "File modified";

        var map = _map;   // snapshot
        map.TryGetValue(matchedPattern, out var paramMap);

        var parts = new List<string>(changes.Count);
        foreach (var (key, (oldVal, newVal)) in changes)
        {
            var label = ResolveLabel(paramMap, key, key.Contains('.') ? key.Split('.', 2)[1] : key);
            parts.Add(string.IsNullOrEmpty(newVal)
                ? $"{label}: removed"
                : string.IsNullOrEmpty(oldVal)
                    ? $"{label}: set to {newVal.Trim()}"
                    : $"{label}: {oldVal.Trim()} → {newVal.Trim()}");
        }
        return string.Join("; ", parts);
    }

    // ── Diff parser ──────────────────────────────────────────────────────────

    // Returns dict: "Section.Key" → (oldValue, newValue). Values are null when only one side exists.
    private static Dictionary<string, (string Old, string New)> ParseDiffChanges(string diffText)
    {
        var removed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var added   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";

        foreach (var rawLine in diffText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // Diff header lines — skip
            if (line.StartsWith("---") || line.StartsWith("+++") || line.StartsWith("@@")) continue;

            // Determine prefix: ' ' = context, '-' = removed, '+' = added
            if (line.Length == 0) continue;
            char prefix = line[0];
            var  body   = line.Length > 1 ? line[1..] : "";

            // Track INI [Section] headers in all lines (context + removed + added)
            var secMatch = _sectionRx.Match(body);
            if (secMatch.Success && prefix != '+')   // sections from old file track the key namespace
                section = secMatch.Groups[1].Value.Trim();

            if (prefix != '-' && prefix != '+') continue;

            var kvMatch = _keyValueRx.Match(line);   // pattern includes the prefix char
            if (!kvMatch.Success) continue;

            var key   = $"{section}.{kvMatch.Groups[2].Value.Trim()}";
            var value = kvMatch.Groups[3].Value.Trim();

            if (prefix == '-') removed[key] = value;
            else               added[key]   = value;
        }

        // Merge: keys present in both → changed; keys only in removed → deleted; only in added → new
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, ov) in removed)
        {
            added.TryGetValue(k, out var nv);
            if (ov != (nv ?? "")) result[k] = (ov, nv ?? "");
        }
        foreach (var (k, nv) in added)
        {
            if (!removed.ContainsKey(k)) result[k] = ("", nv);
        }
        return result;
    }

    private static string ResolveLabel(
        ImmutableDictionary<string, string>? paramMap, string sectionDotKey, string keyOnly)
    {
        if (paramMap is not null)
        {
            if (paramMap.TryGetValue(sectionDotKey, out var label)) return label;
            if (paramMap.TryGetValue(keyOnly,        out label))     return label;
        }
        // Fallback: turn camelCase/PascalCase key into readable words
        return Regex.Replace(keyOnly, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
    }

    // ── JSON schema ──────────────────────────────────────────────────────────

    private record DescriptionsFile(
        string? Version,
        Dictionary<string, Dictionary<string, string>>? Files);

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
```

---

## B.11 — `SqliteRepository.cs` [MODIFIED]

Changes: constructor takes `string dbPath` instead of `IConfiguration`. `EnsureSchema()` adds `file_description` and `change_summary` columns. `MigrateSchema()` adds them to existing databases via `ALTER TABLE`. `InsertAuditEventAsync` writes both new fields. Read queries return both new fields.

```csharp
namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

public class SqliteRepository : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SqliteConnection _readConn;
    private readonly SemaphoreSlim    _writeLock = new(1, 1);
    private readonly ILogger<SqliteRepository> _logger;

    public SqliteRepository(string dbPath, ILogger<SqliteRepository> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        _readConn = new SqliteConnection($"Data Source={dbPath}");
        _readConn.Open();

        using var rp = _readConn.CreateCommand();
        rp.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000;";
        rp.ExecuteNonQuery();

        using var wp = _conn.CreateCommand();
        wp.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
        wp.ExecuteNonQuery();

        using var check = _conn.CreateCommand();
        check.CommandText = "PRAGMA journal_mode=WAL;";
        var mode = check.ExecuteScalar()?.ToString();
        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"SQLite WAL mode could not be enabled (got '{mode}'). " +
                "Ensure the database is not on a network share or FAT32 volume.");

        EnsureSchema();
        logger.LogInformation("SqliteRepository: ready. DB={D}", dbPath);
    }

    // ── Schema ───────────────────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var tx  = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS audit_log (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                changed_at       TEXT    NOT NULL,
                event_type       TEXT    NOT NULL
                                 CHECK(event_type IN ('Created','Modified','Deleted','Renamed')),
                filepath         TEXT    NOT NULL,
                rel_filepath     TEXT    NOT NULL,
                module           TEXT    NOT NULL,
                owner_service    TEXT    NOT NULL,
                monitor_priority TEXT    NOT NULL CHECK (monitor_priority IN ('P1','P2','P3')),
                machine_name     TEXT    NOT NULL,
                sha256_hash      TEXT    NOT NULL,
                old_content      TEXT    NULL,
                diff_text        TEXT    NULL,
                file_description TEXT    NOT NULL DEFAULT '',
                change_summary   TEXT    NOT NULL DEFAULT '',
                is_backfill      INTEGER NOT NULL DEFAULT 0,
                old_filepath     TEXT    NULL
            );

            CREATE INDEX IF NOT EXISTS ix_audit_log_changed_at        ON audit_log (changed_at DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_log_module            ON audit_log (module);
            CREATE INDEX IF NOT EXISTS ix_audit_log_priority          ON audit_log (monitor_priority);
            CREATE INDEX IF NOT EXISTS ix_audit_log_event_type        ON audit_log (event_type);
            CREATE INDEX IF NOT EXISTS ix_audit_log_machine           ON audit_log (machine_name);
            CREATE INDEX IF NOT EXISTS ix_audit_log_owner_service     ON audit_log (owner_service);
            CREATE INDEX IF NOT EXISTS ix_audit_log_rel_filepath      ON audit_log (rel_filepath);
            CREATE INDEX IF NOT EXISTS ix_audit_log_module_changed_at ON audit_log (module, changed_at DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_log_filepath          ON audit_log (filepath);

            CREATE TABLE IF NOT EXISTS file_baselines (
                filepath     TEXT PRIMARY KEY,
                last_hash    TEXT NOT NULL,
                last_seen    TEXT NOT NULL,
                last_content TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_file_baselines_last_seen ON file_baselines (last_seen);

            CREATE TABLE IF NOT EXISTS schema_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT OR IGNORE INTO schema_meta (key, value) VALUES ('schema_version', '3');
            INSERT OR IGNORE INTO schema_meta (key, value) VALUES ('audit_db_version', '1');

            CREATE TABLE IF NOT EXISTS monitor_config (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT OR IGNORE INTO schema_meta (key, value) VALUES
                ('created_at_utc', strftime('%Y-%m-%dT%H:%M:%fZ','now'));
        ";
        cmd.ExecuteNonQuery();
        tx.Commit();

        MigrateSchema();
    }

    // Migrates databases created before the current schema version.
    // ALTER TABLE ADD COLUMN is idempotent-safe only via try/catch (SQLite has no IF NOT EXISTS).
    private void MigrateSchema()
    {
        var version = 1;
        using (var qv = _conn.CreateCommand())
        {
            qv.CommandText = "SELECT value FROM schema_meta WHERE key='schema_version'";
            var raw = qv.ExecuteScalar()?.ToString();
            if (int.TryParse(raw, out var v)) version = v;
        }

        if (version < 2)
        {
            AlterTableAddColumns(new[] { "file_description TEXT NOT NULL DEFAULT ''",
                                         "change_summary   TEXT NOT NULL DEFAULT ''" });
            SetSchemaVersion(2);
            _logger.LogInformation("SqliteRepository: migrated schema to v2 (file_description, change_summary).");
        }

        if (version < 3)
        {
            AlterTableAddColumns(new[] { "is_backfill  INTEGER NOT NULL DEFAULT 0",
                                          "old_filepath TEXT NULL" });
            SetSchemaVersion(3);
            _logger.LogInformation("SqliteRepository: migrated schema to v3 (is_backfill, old_filepath).");
        }
    }

    private void AlterTableAddColumns(string[] columnDefs)
    {
        foreach (var col in columnDefs)
        {
            try
            {
                using var ac = _conn.CreateCommand();
                ac.CommandText = $"ALTER TABLE audit_log ADD COLUMN {col}";
                ac.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (
                ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // column already exists — safe to continue
            }
        }
    }

    private void SetSchemaVersion(int version)
    {
        using var uv = _conn.CreateCommand();
        uv.CommandText = "INSERT OR REPLACE INTO schema_meta (key,value) VALUES ('schema_version',@v)";
        uv.Parameters.AddWithValue("@v", version.ToString());
        uv.ExecuteNonQuery();
    }

    // ── audit_log ────────────────────────────────────────────────────────────

    public async Task InsertAuditEventAsync(AuditLogEntry e, FileBaseline baseline)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var tx = _conn.BeginTransaction();
            using var ins = _conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
                INSERT INTO audit_log
                  (changed_at, event_type, filepath, rel_filepath, module, owner_service,
                   monitor_priority, machine_name, sha256_hash, old_content, diff_text,
                   file_description, change_summary, is_backfill, old_filepath)
                VALUES (@ca,@et,@fp,@rfp,@mod,@svc,@pri,@mn,@hash,@oc,@dt,@fd,@cs,@ib,@ofp)";
            ins.Parameters.AddWithValue("@ca",  e.ChangedAt);
            ins.Parameters.AddWithValue("@et",  e.EventType);
            ins.Parameters.AddWithValue("@fp",  e.Filepath);
            ins.Parameters.AddWithValue("@rfp", e.RelFilepath);
            ins.Parameters.AddWithValue("@mod", e.Module);
            ins.Parameters.AddWithValue("@svc", e.OwnerService);
            ins.Parameters.AddWithValue("@pri", e.MonitorPriority);
            ins.Parameters.AddWithValue("@mn",  e.MachineName);
            ins.Parameters.AddWithValue("@hash",e.Sha256Hash);
            ins.Parameters.AddWithValue("@oc",  (object?)e.OldContent   ?? DBNull.Value);
            ins.Parameters.AddWithValue("@dt",  (object?)e.DiffText     ?? DBNull.Value);
            ins.Parameters.AddWithValue("@fd",  e.FileDescription);
            ins.Parameters.AddWithValue("@cs",  e.ChangeSummary);
            ins.Parameters.AddWithValue("@ib",  e.IsBackfill ? 1 : 0);
            ins.Parameters.AddWithValue("@ofp", (object?)e.OldFilepath  ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync();

            using var upb = _conn.CreateCommand();
            upb.Transaction = tx;
            upb.CommandText = @"
                INSERT INTO file_baselines (filepath, last_hash, last_seen, last_content)
                VALUES (@fp, @lh, @ls, @lc)
                ON CONFLICT(filepath) DO UPDATE SET
                  last_hash    = excluded.last_hash,
                  last_seen    = excluded.last_seen,
                  last_content = excluded.last_content";
            upb.Parameters.AddWithValue("@fp", baseline.Filepath);
            upb.Parameters.AddWithValue("@lh", baseline.LastHash);
            upb.Parameters.AddWithValue("@ls", baseline.LastSeen);
            upb.Parameters.AddWithValue("@lc", (object?)baseline.LastContent ?? DBNull.Value);
            await upb.ExecuteNonQueryAsync();

            tx.Commit();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Update a baseline entry without writing an audit event (used for unchanged files in CatchUpScanner).</summary>
    public async Task UpsertBaselineAsync(FileBaseline baseline)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO file_baselines (filepath, last_hash, last_seen, last_content)
                VALUES (@fp, @lh, @ls, @lc)
                ON CONFLICT(filepath) DO UPDATE SET
                  last_hash    = excluded.last_hash,
                  last_seen    = excluded.last_seen,
                  last_content = excluded.last_content";
            cmd.Parameters.AddWithValue("@fp", baseline.Filepath);
            cmd.Parameters.AddWithValue("@lh", baseline.LastHash);
            cmd.Parameters.AddWithValue("@ls", baseline.LastSeen);
            cmd.Parameters.AddWithValue("@lc", (object?)baseline.LastContent ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Load MonitorConfig from the monitor_config table.
    /// Inserts defaults on first run (empty table) so the row-set always exists.
    /// </summary>
    public MonitorConfig LoadConfig()
    {
        var defaults = new MonitorConfig();
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["watch_path"]                  = defaults.WatchPath,
            ["global_db_path"]              = defaults.GlobalDbPath,
            ["classification_rules_path"]   = defaults.ClassificationRulesPath,
            ["parameter_descriptions_path"] = defaults.ParameterDescriptionsPath,
            ["api_port"]                    = defaults.ApiPort.ToString(),
            ["api_bind_address"]            = defaults.ApiBindAddress,
            ["debounce_ms"]                 = defaults.DebounceMs.ToString(),
            ["fsw_buffer_bytes"]            = defaults.FswBufferBytes.ToString(),
            ["max_content_bytes"]           = defaults.MaxContentBytes.ToString(),
            ["capture_content"]             = defaults.CaptureContent.ToString(),
            ["catch_up_yield_threshold"]    = defaults.CatchUpYieldThreshold.ToString(),
            ["recovery_delay_ms"]           = defaults.RecoveryDelayMs.ToString()
        };

        // Insert defaults on first run (INSERT OR IGNORE — does not overwrite user settings)
        _writeLock.Wait();
        try
        {
            using var tx = _conn.BeginTransaction();
            foreach (var (k, v) in data)
            {
                using var ins = _conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT OR IGNORE INTO monitor_config (key, value) VALUES (@k, @v)";
                ins.Parameters.AddWithValue("@k", k);
                ins.Parameters.AddWithValue("@v", v);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }
        finally { _writeLock.Release(); }

        // Read all config (may now include user-edited values)
        using var cmd = _readConn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM monitor_config";
        using var r = cmd.ExecuteReader();
        while (r.Read()) data[r.GetString(0)] = r.GetString(1);

        var cfg = new MonitorConfig();
        if (data.TryGetValue("watch_path",                  out var s)) cfg.WatchPath                 = s;
        if (data.TryGetValue("global_db_path",              out s))     cfg.GlobalDbPath               = s;
        if (data.TryGetValue("classification_rules_path",   out s))     cfg.ClassificationRulesPath    = s;
        if (data.TryGetValue("parameter_descriptions_path", out s))     cfg.ParameterDescriptionsPath  = s;
        if (data.TryGetValue("api_bind_address",            out s))     cfg.ApiBindAddress             = s;
        if (data.TryGetValue("api_port",           out s) && int.TryParse(s,  out var i)) cfg.ApiPort                  = i;
        if (data.TryGetValue("debounce_ms",        out s) && int.TryParse(s,  out i))     cfg.DebounceMs               = i;
        if (data.TryGetValue("fsw_buffer_bytes",   out s) && int.TryParse(s,  out i))     cfg.FswBufferBytes           = i;
        if (data.TryGetValue("catch_up_yield_threshold", out s) && int.TryParse(s, out i)) cfg.CatchUpYieldThreshold   = i;
        if (data.TryGetValue("recovery_delay_ms",  out s) && int.TryParse(s,  out i))     cfg.RecoveryDelayMs         = i;
        if (data.TryGetValue("max_content_bytes",  out s) && long.TryParse(s, out var l)) cfg.MaxContentBytes         = l;
        if (data.TryGetValue("capture_content",    out s) && bool.TryParse(s, out var b)) cfg.CaptureContent          = b;
        // MachineName always reflects the actual machine — not from stored config
        return cfg;
    }

    // ── file_baselines ───────────────────────────────────────────────────────

    public async Task<FileBaseline?> GetBaselineAsync(string filepath)
    {
        using var cmd = _readConn.CreateCommand();
        cmd.CommandText =
            "SELECT filepath, last_hash, last_seen, last_content " +
            "FROM file_baselines WHERE filepath=@fp";
        cmd.Parameters.AddWithValue("@fp", filepath);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new FileBaseline
        {
            Filepath    = r.GetString(0),
            LastHash    = r.GetString(1),
            LastSeen    = r.GetString(2),
            LastContent = r.IsDBNull(3) ? null : r.GetString(3)
        };
    }

    public async Task<List<FileBaseline>> GetAllBaselinesAsync()
    {
        var list = new List<FileBaseline>();
        using var cmd = _readConn.CreateCommand();
        cmd.CommandText =
            "SELECT filepath, last_hash, last_seen, last_content " +
            "FROM file_baselines";
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new FileBaseline
            {
                Filepath    = r.GetString(0),
                LastHash    = r.GetString(1),
                LastSeen    = r.GetString(2),
                LastContent = r.IsDBNull(3) ? null : r.GetString(3)
            });
        return list;
    }

    public async Task DeleteBaselineAsync(string filepath)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM file_baselines WHERE filepath=@fp";
            cmd.Parameters.AddWithValue("@fp", filepath);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        _conn.Dispose();
        _readConn.Dispose();
    }
}
```

---

## B.12 — `ShardRegistry.cs` [NEW]

Manages one `SqliteRepository` per job folder. Created lazily on first event for that job.

```csharp
namespace FalconAuditService;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

public class ShardRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, SqliteRepository?> _shards =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ShardRegistry> _logger;

    public ShardRegistry(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger        = loggerFactory.CreateLogger<ShardRegistry>();
    }

    /// <summary>
    /// Return the SqliteRepository for a job, creating it on first call.
    /// The shard file lives at &lt;jobPath&gt;\.audit\audit.db.
    /// Returns null if the shard cannot be opened (REL-007); callers must null-check.
    /// Failures are NOT cached — the next event for the same job will retry.
    /// </summary>
    public SqliteRepository? GetOrCreate(string jobName, string jobPath)
    {
        // Fast path: already open
        if (_shards.TryGetValue(jobName, out var existing)) return existing;

        var auditDir = Path.Combine(jobPath, ".audit");
        var dbPath   = Path.Combine(auditDir, "audit.db");
        try
        {
            Directory.CreateDirectory(auditDir);
            _logger.LogInformation("ShardRegistry: opening shard for job '{J}' at {D}", jobName, dbPath);
            var repo = new SqliteRepository(dbPath, _loggerFactory.CreateLogger<SqliteRepository>());
            _shards.TryAdd(jobName, repo);   // if two threads raced, one wins; loser discards
            return _shards[jobName];         // return whichever won
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ShardRegistry: failed to open shard for {J}; NOT cached. DB={D}", jobName, dbPath);
            return null;   // do NOT cache — next event will retry
        }
    }

    public bool TryGet(string jobName, out SqliteRepository? repo) =>
        _shards.TryGetValue(jobName, out repo);

    /// <summary>Close and remove the shard for a job (e.g., job folder deleted).</summary>
    public void Remove(string jobName)
    {
        if (_shards.TryRemove(jobName, out var repo))
        {
            _logger.LogInformation("ShardRegistry: closed shard for job '{J}'.", jobName);
            repo.Dispose();
        }
    }

    public IEnumerable<string> JobNames => _shards.Keys;

    public void Dispose()
    {
        foreach (var repo in _shards.Values)
            repo.Dispose();
        _shards.Clear();
    }
}
```

---

## B.13 — `ManifestManager.cs` [NEW]

Reads and writes `.audit\manifest.json`. All writes go through an atomic temp-file rename.

```csharp
namespace FalconAuditService;

using System.Collections.Concurrent;
using System.Text.Json;
using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

public class ManifestManager
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
            var manifest = ReadManifest(manifestPath);
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
    public void RecordArrival(string jobPath, string machineName)
    {
        var auditDir     = Path.Combine(jobPath, ".audit");
        var manifestPath = Path.Combine(auditDir, "manifest.json");
        var jobName      = Path.GetFileName(jobPath.TrimEnd('\\', '/'));

        var sem = LockFor(manifestPath);
        sem.Wait();
        try
        {
            var manifest = ReadManifest(manifestPath) ?? new JobManifest
            {
                JobName  = jobName,
                Created  = new MachineTimestamp { Machine = machineName, At = DateTime.UtcNow }
            };

            var last = manifest.History.LastOrDefault();

            // If last entry is from a different machine and still open, close it
            if (last?.To == null && !string.Equals(last?.Machine, machineName,
                                                    StringComparison.OrdinalIgnoreCase))
            {
                last!.To = DateTime.UtcNow;
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
                    From    = DateTime.UtcNow,
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
    public void RecordDeparture(string jobPath)
    {
        var manifestPath = Path.Combine(jobPath, ".audit", "manifest.json");

        var sem = LockFor(manifestPath);
        sem.Wait();
        try
        {
            var manifest = ReadManifest(manifestPath);
            if (manifest is null) return;

            var last = manifest.History.LastOrDefault();
            if (last?.To == null)
            {
                last!.To = DateTime.UtcNow;
                WriteManifest(manifestPath, manifest);
                _logger.LogInformation(
                    "ManifestManager: departure recorded for job '{J}'.",
                    Path.GetFileName(jobPath.TrimEnd('\\', '/')));
            }
        }
        finally { sem.Release(); }
    }

    private JobManifest? ReadManifest(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<JobManifest>(
                File.ReadAllText(path), _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ManifestManager: could not read {P}", path);
            return null;
        }
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
```

---

## B.14 — `DirectoryWatcher.cs` [NEW]

Watches `c:\job\` at depth=1 (job folders only, not file changes). Fires callbacks on job folder arrive/remove.

```csharp
namespace FalconAuditService;

using Microsoft.Extensions.Logging;

public class DirectoryWatcher : IDisposable
{
    private FileSystemWatcher?            _watcher;
    private readonly string               _watchPath;
    private readonly Action<string, string> _onArrived;   // (jobName, jobFullPath)
    private readonly Action<string>       _onDeparted;    // (jobName)
    private readonly ILogger<DirectoryWatcher> _logger;

    public DirectoryWatcher(
        string watchPath,
        Action<string, string> onArrived,
        Action<string> onDeparted,
        ILogger<DirectoryWatcher> logger)
    {
        _watchPath  = watchPath;
        _onArrived  = onArrived;
        _onDeparted = onDeparted;
        _logger     = logger;
    }

    public void Start()
    {
        _watcher = new FileSystemWatcher(_watchPath)
        {
            NotifyFilters         = NotifyFilters.DirectoryName,
            IncludeSubdirectories = false,   // depth=1 — job directories only
            EnableRaisingEvents   = true
        };
        _watcher.Created += OnCreated;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _logger.LogInformation("DirectoryWatcher: watching {P} for job folder changes.", _watchPath);
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Enumerate existing job folders at startup — fires onArrived for each.</summary>
    public void EnumerateExisting()
    {
        foreach (var dir in Directory.EnumerateDirectories(_watchPath))
        {
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name))
                _onArrived(name, dir);
        }
    }

    private void OnCreated(object _, FileSystemEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Name)) return;
        _logger.LogInformation("DirectoryWatcher: job folder arrived — '{N}'.", e.Name);
        _onArrived(e.Name!, e.FullPath);
    }

    private void OnDeleted(object _, FileSystemEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Name)) return;
        _logger.LogInformation("DirectoryWatcher: job folder departed — '{N}'.", e.Name);
        _onDeparted(e.Name!);
    }

    private void OnRenamed(object _, RenamedEventArgs e)
    {
        _logger.LogInformation("DirectoryWatcher: job folder renamed '{O}' → '{N}'.",
                                e.OldName, e.Name);
        if (!string.IsNullOrEmpty(e.OldName)) _onDeparted(e.OldName!);
        if (!string.IsNullOrEmpty(e.Name))    _onArrived(e.Name!, e.FullPath);
    }

    public void Dispose() => _watcher?.Dispose();
}
```

---

## B.15 — `ChangeEvent.cs` [UNCHANGED]

```csharp
namespace FalconAuditService;

internal record ChangeEvent(
    string             FullPath,
    WatcherChangeTypes ChangeType,
    DateTime           DetectedAt,
    string?            OldPath = null   // populated for Renamed events only
);
```

---

## B.16 — `FileChangeHandler.cs` [MODIFIED]

Changes: takes `ShardRegistry` and `SqliteRepository` (global). Routes events to the correct shard based on job name extracted from the file path. Global files (`c:\job\status.ini` — no subdirectory) go to `globalRepo`. `ChangeDescriptionEnricher` injected to populate `FileDescription` and `ChangeSummary` on every `AuditLogEntry`.

```csharp
namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

public class FileChangeHandler
{
    private readonly ShardRegistry            _shards;
    private readonly SqliteRepository         _globalRepo;
    private readonly FileClassifier           _classifier;
    private readonly ContentCache             _contentCache;
    private readonly ManifestManager          _manifest;
    private readonly ChangeDescriptionEnricher _enricher;
    private readonly MonitorConfig            _config;
    private readonly ILogger<FileChangeHandler> _logger;

    public FileChangeHandler(
        ShardRegistry shards, SqliteRepository globalRepo,
        FileClassifier classifier, ContentCache contentCache,
        ManifestManager manifest, ChangeDescriptionEnricher enricher,
        MonitorConfig config, ILogger<FileChangeHandler> logger)
    {
        _shards       = shards;
        _globalRepo   = globalRepo;
        _classifier   = classifier;
        _contentCache = contentCache;
        _manifest     = manifest;
        _enricher     = enricher;
        _config       = config;
        _logger       = logger;
    }

    public async Task HandleAsync(ChangeEvent ev)
    {
        _logger.LogDebug("Processing change. Path={P} ChangeType={T}", ev.FullPath, ev.ChangeType);

        var repo     = GetRepo(ev.FullPath);
        var cls      = _classifier.Classify(ev.FullPath);
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
                oldContent = _contentCache.Get(ev.FullPath);
                break;

            case WatcherChangeTypes.Created:
            case WatcherChangeTypes.Changed:
                newHash = HashHelper.ComputeSha256(ev.FullPath);
                if (newHash is null)
                {
                    _logger.LogWarning("Could not hash {P} — skipping.", ev.FullPath);
                    return;
                }
                _logger.LogDebug("Hash computed. OldHash={O} NewHash={N} HashChanged={C}",
                                  oldHash?[..8] ?? "null", newHash[..8], newHash != oldHash);

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
                newHash    = HashHelper.ComputeSha256(ev.FullPath);
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

        var (jobName, jobPath) = ExtractJob(ev.FullPath);
        var watch = _config.WatchPath.TrimEnd('\\', '/');
        var relFilepath = ev.FullPath.StartsWith(watch, StringComparison.OrdinalIgnoreCase)
            ? ev.FullPath[(watch.Length)..].TrimStart('\\', '/')
            : ev.FullPath;

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
            OldFilepath     = changeType == "Renamed" ? ev.OldPath : null
        };

        var bl = MakeBaseline(ev.FullPath, newHash ?? oldHash ?? "", oldContent);
        await repo.InsertAuditEventAsync(entry, bl);

        _logger.LogInformation(
            "Audit event written. File={F} EventType={C} Module={M} Priority={P}",
            Path.GetFileName(ev.FullPath), changeType, cls.Module, cls.MonitorPriority);

        // Wire manifest event counter (MFT-007)
        if (jobPath is not null)
            await _manifest.IncrementEventsAsync(jobPath);

        if (ev.ChangeType == WatcherChangeTypes.Deleted)
        {
            await repo.DeleteBaselineAsync(ev.FullPath);
            _contentCache.Remove(ev.FullPath);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Return the shard repo for the job that owns this file path.
    /// Files directly under c:\job\ (e.g. status.ini) go to globalRepo.
    /// </summary>
    private SqliteRepository GetRepo(string filePath)
    {
        var (jobName, jobPath) = ExtractJob(filePath);
        if (jobName is null || jobPath is null) return _globalRepo;
        return _shards.GetOrCreate(jobName, jobPath) ?? _globalRepo;
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
```

---

## B.17 — `FileMonitorService.cs` [UNCHANGED]

```csharp
namespace FalconAuditService;

using System.Collections.Concurrent;
using System.Threading.Channels;
using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

public class FileMonitorService : IDisposable
{
    private FileSystemWatcher?                                     _watcher;
    private readonly ConcurrentDictionary<string, Timer>           _debounce    = new();
    private readonly ConcurrentDictionary<string, FileSystemEventArgs> _latestEvent = new();
    private int _recoveryScheduled;
    private readonly Channel<ChangeEvent> _queue = Channel.CreateBounded<ChangeEvent>(
        new BoundedChannelOptions(1024)
        {
            FullMode     = BoundedChannelFullMode.Wait,   // back-pressure on producer
            SingleReader = false,                          // multiple consumers
            SingleWriter = false
        });
    private Task[]? _consumers;
    private readonly FileChangeHandler  _handler;
    private readonly CatchUpScanner     _catchUp;
    private readonly MonitorConfig      _config;
    private readonly ILogger<FileMonitorService> _logger;
    private CancellationToken           _ct;

    public bool IsActive => _watcher?.EnableRaisingEvents == true;

    public FileMonitorService(FileChangeHandler handler, CatchUpScanner catchUp,
                               MonitorConfig config, ILogger<FileMonitorService> logger)
    {
        _handler = handler;
        _catchUp = catchUp;
        _config  = config;
        _logger  = logger;
    }

    public void Start(CancellationToken ct)
    {
        _ct = ct;
        InitWatcher();

        int workerCount = Math.Max(2, Environment.ProcessorCount);
        _consumers = Enumerable.Range(0, workerCount)
                     .Select(_ => Task.Run(ConsumeAsync, ct))
                     .ToArray();
        _logger.LogInformation(
            "FileMonitorService: FSW enabled. Path={P} Buffer={B} Workers={W}",
            _config.WatchPath, _config.FswBufferBytes, workerCount);
    }

    public async Task StopAsync()
    {
        _watcher?.Dispose();
        _queue.Writer.TryComplete();
        if (_consumers is not null)
            await Task.WhenAll(_consumers).WaitAsync(TimeSpan.FromSeconds(10));
        _logger.LogInformation("FileMonitorService stopped.");
    }

    // Keep synchronous Stop() for backward compat with Worker.cs StopAsync
    public void Stop() => StopAsync().GetAwaiter().GetResult();

    private void InitWatcher()
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(_config.WatchPath)
        {
            NotifyFilters         = NotifyFilters.FileName
                                  | NotifyFilters.LastWrite
                                  | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            InternalBufferSize    = _config.FswBufferBytes,
            Filter                = "*.*",
            EnableRaisingEvents   = true
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Error   += OnError;
    }

    private void OnFileEvent(object _, FileSystemEventArgs e)
    {
        _logger.LogDebug("FSW event received. Type={T} Path={P}", e.ChangeType, e.FullPath);
        // Always record the latest event so FireDebounce dispatches the most recent change type.
        _latestEvent[e.FullPath] = e;
        _debounce.AddOrUpdate(
            e.FullPath,
            key =>
            {
                _logger.LogDebug("FSW debounce created. Path={P}", key);
                return new Timer(FireDebounce, key, _config.DebounceMs, Timeout.Infinite);
            },
            (key, existing) =>
            {
                existing.Change(_config.DebounceMs, Timeout.Infinite);
                _logger.LogDebug("FSW event received. Type={T} Path={P}  (debounce reset)",
                                  e.ChangeType, e.FullPath);
                return existing;
            });
    }

    private void OnRenamed(object _, RenamedEventArgs e)
    {
        _logger.LogDebug("FSW event received. Type=Renamed OldPath={O} NewPath={N}",
                          e.OldFullPath, e.FullPath);
        _ = TryEnqueueAsync(new ChangeEvent(e.FullPath, WatcherChangeTypes.Renamed,
                                            DateTime.UtcNow, e.OldFullPath));
    }

    private void FireDebounce(object? state)
    {
        var key = (string)state!;
        if (_debounce.TryRemove(key, out var t)) t.Dispose();
        if (!_latestEvent.TryRemove(key, out var e)) return;   // should always succeed
        _logger.LogDebug("Debounce fired. Path={P} FinalType={T}  Enqueued.", key, e.ChangeType);
        _ = TryEnqueueAsync(new ChangeEvent(e.FullPath, e.ChangeType, DateTime.UtcNow));
    }

    private void OnError(object _, ErrorEventArgs e)
    {
        _logger.LogWarning("FSW buffer overflow or error: {M}. Restarting watcher.",
                            e.GetException().Message);
        InitWatcher();

        // Debounce the recovery scan: coalesce rapid overflow events into a single full re-hash.
        if (Interlocked.Exchange(ref _recoveryScheduled, 1) == 0)
        {
            _ = Task.Delay(_config.RecoveryDelayMs, _ct).ContinueWith(_ =>
            {
                Interlocked.Exchange(ref _recoveryScheduled, 0);
                _logger.LogInformation("FSW overflow recovery: starting catch-up scan.");
                _ = _catchUp.RunAllJobsParallelAsync(_ct);
            }, TaskScheduler.Default);
        }
    }

    private async Task TryEnqueueAsync(ChangeEvent ev)
    {
        // Wait up to 1 s; if still full, log + trigger CatchUp recovery (REL-001).
        using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_ct, cts.Token);
        try
        {
            await _queue.Writer.WriteAsync(ev, linked.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Audit event queue full — triggering CatchUpScanner. DroppedPath={P}", ev.FullPath);
            _ = Task.Run(() => _catchUp.RunAllJobsParallelAsync(_ct));
        }
    }

    private async Task ConsumeAsync()
    {
        await foreach (var ev in _queue.Reader.ReadAllAsync(_ct))
        {
            try { await _handler.HandleAsync(ev); }
            catch (Exception ex) { _logger.LogError(ex, "Error processing event. Path={P}", ev.FullPath); }
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        foreach (var t in _debounce.Values) t.Dispose();
        _latestEvent.Clear();
    }
}
```

---

## B.18 — `CatchUpScanner.cs` [MODIFIED]

Changes: takes `ShardRegistry` and global `SqliteRepository`. `RunAsync` accepts optional `jobPath` to scope the scan to a single job. Internal `GetRepo(path)` routes writes to the correct shard or global repo.

```csharp
namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

public class CatchUpScanner
{
    private readonly ShardRegistry             _shards;
    private readonly SqliteRepository          _globalRepo;
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

    public CatchUpScanner(ShardRegistry shards, SqliteRepository globalRepo,
                           FileClassifier classifier, ContentCache contentCache,
                           ChangeDescriptionEnricher enricher,
                           MonitorConfig config, ILogger<CatchUpScanner> logger)
    {
        _shards       = shards;
        _globalRepo   = globalRepo;
        _classifier   = classifier;
        _contentCache = contentCache;
        _enricher     = enricher;
        _config       = config;
        _logger       = logger;
    }

    /// <summary>
    /// Run catch-up scans for all jobs in parallel (SVC-007, PERF-004, CUS-006).
    /// Each job runs in its own Task; per-shard SemaphoreSlim(1) serialises writes.
    /// </summary>
    public async Task RunAllJobsParallelAsync(CancellationToken ct)
    {
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
        var sw       = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("CatchUpScanner: starting reconciliation scan. Root={R}", scanRoot);

        var currentFiles = Directory
            .EnumerateFiles(scanRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => IncludedExts.Contains(Path.GetExtension(f)))
            .ToList();

        _logger.LogInformation("CatchUpScanner: found {N} candidate files.", currentFiles.Count);

        // Build a per-repo baseline map: only load baselines from repos we'll scan
        // For a scoped job scan, get baselines only from that job's shard.
        var repo         = jobPath is not null ? GetRepo(Path.Combine(jobPath, "_dummy")) : null;
        var allBaselines = repo is not null
            ? await repo.GetAllBaselinesAsync()
            : await GetAllBaselinesAsync(currentFiles);

        var baselineMap  = allBaselines.ToDictionary(b => b.Filepath,
                                                      StringComparer.OrdinalIgnoreCase);
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
            var fileRepo = GetRepo(path);
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
                    IsBackfill      = true
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
                    IsBackfill      = true
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
            var cls2     = _classifier.Classify(bl.Filepath);
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
                IsBackfill      = true
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

        sw.Stop();
        _logger.LogInformation(
            "CatchUpScanner: complete. Unchanged={U} Created={C} Modified={M} Deleted={D} Elapsed={E}ms",
            unchanged, created, modified, deleted, sw.ElapsedMilliseconds);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private SqliteRepository GetRepo(string filePath)
    {
        var watch = _config.WatchPath.TrimEnd('\\', '/');
        if (!filePath.StartsWith(watch, StringComparison.OrdinalIgnoreCase))
            return _globalRepo;

        var relative = filePath[(watch.Length)..].TrimStart('\\', '/');
        var sep      = relative.IndexOfAny(new[] { '\\', '/' });
        if (sep <= 0) return _globalRepo;

        var jobName = relative[..sep];
        var jobPath = Path.Combine(watch, jobName);
        return _shards.GetOrCreate(jobName, jobPath) ?? _globalRepo;
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

        foreach (var f in currentFiles)
        {
            var watch = _config.WatchPath.TrimEnd('\\', '/');
            var rel   = f[(watch.Length)..].TrimStart('\\', '/');
            var sep   = rel.IndexOfAny(new[] { '\\', '/' });
            if (sep > 0) jobNames.Add(rel[..sep]);
        }

        result.AddRange(await _globalRepo.GetAllBaselinesAsync());

        foreach (var jn in jobNames)
        {
            var jp   = Path.Combine(_config.WatchPath, jn);
            var repo = _shards.GetOrCreate(jn, jp);
            result.AddRange(await repo.GetAllBaselinesAsync());
        }
        return result;
    }

    private async Task<string?> ReadIfP1Async(string path, string priority, long size)
    {
        if (priority != "P1" || !_config.StoreContentP1) return null;
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
```

---

## B.19 — `Worker.cs` [MODIFIED]

Changes: enumerates existing job folders at startup via `DirectoryWatcher.EnumerateExisting()`; wires `DirectoryWatcher` callbacks for live job folder arrivals; calls `ManifestManager.RecordDeparture()` on stop.

```csharp
namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class Worker : BackgroundService
{
    private readonly FileMonitorService _monitor;
    private readonly CatchUpScanner     _catchUp;
    private readonly ShardRegistry      _shards;
    private readonly ManifestManager    _manifest;
    private readonly DirectoryWatcher   _dirWatcher;
    private readonly MonitorConfig      _config;
    private readonly ILogger<Worker>    _logger;

    public Worker(
        FileMonitorService monitor, CatchUpScanner catchUp,
        ShardRegistry shards, ManifestManager manifest,
        DirectoryWatcher dirWatcher, MonitorConfig config,
        ILogger<Worker> logger)
    {
        _monitor    = monitor;
        _catchUp    = catchUp;
        _shards     = shards;
        _manifest   = manifest;
        _dirWatcher = dirWatcher;
        _config     = config;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FalconAuditService starting. WatchPath={W}", _config.WatchPath);
        if (!Directory.Exists(_config.WatchPath))
        {
            _logger.LogCritical("WatchPath does not exist: {P}", _config.WatchPath);
            return;
        }

        // Step 1: register the recursive FSW BEFORE any catch-up work (SVC-003, PERF-001)
        _monitor.Start(stoppingToken);
        _dirWatcher.Start();
        _logger.LogInformation("FalconAuditService FSW live.");

        // Step 2: enumerate existing job folders (opens shards, records arrival in manifest)
        _dirWatcher.EnumerateExisting();

        // Step 3: run catch-up scan in PARALLEL across all jobs (SVC-007). Runs after FSW
        // is already live, so any live event during catch-up is queued and processed.
        _ = Task.Run(async () =>
        {
            using var scanTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(
                                    stoppingToken, scanTimeout.Token);
            try
            {
                await _catchUp.RunAllJobsParallelAsync(scanCts.Token);
                _logger.LogInformation("CatchUpScanner: full reconciliation complete.");
            }
            catch (OperationCanceledException) when (scanTimeout.IsCancellationRequested)
            {
                _logger.LogWarning("CatchUpScanner exceeded 5-min limit.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CatchUpScanner failed.");
            }
        }, stoppingToken);

        _logger.LogInformation("FalconAuditService running.");
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (TaskCanceledException) { /* normal shutdown */ }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("StopAsync requested. Draining queue.");

        // Record departure in manifest for every active job
        foreach (var jobName in _shards.JobNames)
        {
            var jobPath = Path.Combine(_config.WatchPath, jobName);
            _manifest.RecordDeparture(jobPath);
        }

        _dirWatcher.Stop();
        _monitor.Stop();
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("FalconAuditService stopped.");
    }
}
```

---

## B.20 — `Program.cs` [MODIFIED]

Changes: merged with `FalconAuditWebServer` — `WebApplication.CreateBuilder` hosts both the audit `BackgroundService` (`Worker`) and the Kestrel REST API. `UseWindowsService()` enables SCM registration. `AppContext.BaseDirectory` fixes Serilog bootstrap path when running as Windows Service (CWD is `System32` when launched by SCM).

```csharp
using FalconAuditService;
using FalconAuditService.Models;
using FalconAuditWebServer.Endpoints;
using FalconAuditWebServer.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
        .Build())
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseWindowsService(o => o.ServiceName = "FalconAuditService");
    builder.Host.UseSerilog();

    // ── Audit service dependencies ─────────────────────────────────────────
    builder.Services.AddSingleton(sp =>
    {
        var globalDbPath = builder.Configuration["AuditService:GlobalDbPath"] ?? @"C:\bis\auditlog\global.db";
        return new SqliteRepository(globalDbPath,
            sp.GetRequiredService<ILogger<SqliteRepository>>());
    });

    builder.Services.AddSingleton(sp =>
    {
        var cfg     = sp.GetRequiredService<SqliteRepository>().LoadConfig();
        var section = builder.Configuration.GetSection("AuditService");
        var rules   = section["ClassificationRulesPath"];
        var param   = section["ParameterDescriptionsPath"];
        var global  = section["GlobalDbPath"];
        if (!string.IsNullOrEmpty(rules))  cfg.ClassificationRulesPath   = rules;
        if (!string.IsNullOrEmpty(param))  cfg.ParameterDescriptionsPath = param;
        if (!string.IsNullOrEmpty(global)) cfg.GlobalDbPath              = global;
        return cfg;
    });

    builder.Services.AddSingleton<ContentCache>();
    builder.Services.AddSingleton<ShardRegistry>();
    builder.Services.AddSingleton<ManifestManager>();

    builder.Services.AddSingleton(sp =>
    {
        var config     = sp.GetRequiredService<MonitorConfig>();
        var classifier = new FileClassifier(sp.GetRequiredService<ILogger<FileClassifier>>());
        classifier.LoadRules(config.ClassificationRulesPath);
        return classifier;
    });

    builder.Services.AddSingleton(sp =>
    {
        var config   = sp.GetRequiredService<MonitorConfig>();
        var enricher = new ChangeDescriptionEnricher(sp.GetRequiredService<ILogger<ChangeDescriptionEnricher>>());
        enricher.Load(config.ParameterDescriptionsPath);
        return enricher;
    });

    builder.Services.AddSingleton(sp =>
    {
        var config   = sp.GetRequiredService<MonitorConfig>();
        var shards   = sp.GetRequiredService<ShardRegistry>();
        var manifest = sp.GetRequiredService<ManifestManager>();
        var logger   = sp.GetRequiredService<ILogger<DirectoryWatcher>>();
        return new DirectoryWatcher(config.WatchPath,
            onArrived: (jobName, jobPath) =>
            {
                shards.GetOrCreate(jobName, jobPath);
                manifest.RecordArrival(jobPath, config.MachineName);
            },
            onDeparted: (jobName) =>
            {
                manifest.RecordDeparture(Path.Combine(config.WatchPath, jobName));
                shards.Remove(jobName);
            },
            logger);
    });

    builder.Services.AddSingleton<FileChangeHandler>();
    builder.Services.AddSingleton<CatchUpScanner>();
    builder.Services.AddSingleton<FileMonitorService>();
    builder.Services.AddHostedService<Worker>();

    // ── Web server dependencies ────────────────────────────────────────────
    builder.Services.AddSingleton<JobDiscoveryService>();
    builder.Services.AddSingleton<QueryRepository>();

    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddAuthorization(o =>
    {
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        o.AddPolicy("AuditorOnly", p => p.RequireRole("Auditor"));
    });

    var app = builder.Build();

    app.UseAuthentication();
    app.UseAuthorization();

    var api = app.MapGroup("/api");
    JobsEndpoints.Map(api);
    EventsEndpoints.Map(api);
    FileHistoryEndpoints.Map(api);

    app.Run();
}
catch (Exception ex) { Log.Fatal(ex, "FalconAuditService terminated unexpectedly."); }
finally { Log.CloseAndFlush(); }
```

---

## B.21 — `install.ps1` [UNCHANGED]

```powershell
<#
.SYNOPSIS
    Install or uninstall the FalconAuditService Windows Service.
.EXAMPLE
    .\install.ps1 -Action Install
    .\install.ps1 -Action Uninstall
#>
#Requires -RunAsAdministrator

param(
    [ValidateSet('Install','Uninstall')]
    [string]$Action      = 'Install',
    [string]$InstallPath = 'C:\bis\bin\FalconAuditService',
    [string]$DbPath      = 'C:\bis\auditlog'
)

$ServiceName = 'FalconAuditService'
$DisplayName = 'Falcon Audit Log Service'
$Description = 'Monitors c:\job\ for file changes and writes per-job audit shards to SQLite.'
$ExePath     = Join-Path $InstallPath 'FalconAuditService.exe'
$DbDir       = $DbPath

if ($Action -eq 'Install') {
    if (-not (Test-Path $ExePath)) {
        Write-Error "Executable not found: $ExePath"
        exit 1
    }

    if (-not (Test-Path $DbDir)) {
        New-Item -ItemType Directory -Path $DbDir | Out-Null
        Write-Host "Created directory: $DbDir"
    }

    # Copy FileClassificationRules.json and ParameterDescriptions.json on first install
    $rulesSource = Join-Path $InstallPath 'FileClassificationRules.json'
    $rulesDest   = Join-Path $DbDir 'FileClassificationRules.json'
    if ((Test-Path $rulesSource) -and -not (Test-Path $rulesDest)) {
        Copy-Item $rulesSource $rulesDest
        Write-Host "Installed FileClassificationRules.json to $DbDir"
    }
    $pdSource = Join-Path $InstallPath 'ParameterDescriptions.json'
    $pdDest   = Join-Path $DbDir 'ParameterDescriptions.json'
    if ((Test-Path $pdSource) -and -not (Test-Path $pdDest)) {
        Copy-Item $pdSource $pdDest
        Write-Host "Installed ParameterDescriptions.json to $DbDir"
    }

    # Grant the virtual service account least-privilege access (read on C:\job\, write on C:\bis\auditlog\)
    icacls "C:\job"           /grant "NT SERVICE\FalconAuditSvc:(OI)(CI)R" /T | Out-Null
    icacls "C:\bis\auditlog"  /grant "NT SERVICE\FalconAuditSvc:(OI)(CI)M" /T | Out-Null
    Write-Host "ACLs set for NT SERVICE\FalconAuditSvc."

    sc.exe create $ServiceName `
        binPath= "`"$ExePath`"" `
        start=   auto `
        obj=     "NT SERVICE\FalconAuditSvc"

    sc.exe description $ServiceName $Description
    sc.exe failure      $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000

    Start-Service -Name $ServiceName
    Write-Host "Service '$ServiceName' installed and started."

} elseif ($Action -eq 'Uninstall') {
    if ((Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)?.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Write-Host "Service stopped."
    }
    sc.exe delete $ServiceName
    Write-Host "Service '$ServiceName' uninstalled."
}
```

---

## B.22 — `Services/JobDiscoveryService.cs` [NEW]

Polls `AuditService:WatchPath` every 30 s. Maintains a `volatile IReadOnlyList<string>` of known jobs (directories that contain `.audit\audit.db`). Used by `QueryRepository` to enumerate jobs and locate shard paths.

```csharp
namespace FalconAuditWebServer.Services;

using FalconAuditWebServer.Models;
using Microsoft.Data.Sqlite;

public class JobDiscoveryService : IDisposable
{
    private readonly string _watchPath;
    private readonly string _globalDb;
    private readonly ILogger<JobDiscoveryService> _logger;
    private readonly Timer _refreshTimer;
    private volatile IReadOnlyList<string> _knownJobs = Array.Empty<string>();

    public JobDiscoveryService(IConfiguration cfg, ILogger<JobDiscoveryService> logger)
    {
        _watchPath   = cfg["AuditService:WatchPath"]    ?? @"C:\job";
        _globalDb    = cfg["AuditService:GlobalDbPath"] ?? @"C:\bis\auditlog\global.db";
        _logger      = logger;
        Refresh();
        _refreshTimer = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public IReadOnlyList<string> KnownJobs => _knownJobs;
    public string WatchPath => _watchPath;
    public string GlobalDb  => _globalDb;

    public void Refresh()
    {
        try
        {
            var jobs = Directory.EnumerateDirectories(_watchPath)
                .Where(d => File.Exists(Path.Combine(d, ".audit", "audit.db")))
                .Select(d => Path.GetFileName(d)!)
                .ToList();
            _knownJobs = jobs;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "JobDiscoveryService: refresh failed."); }
    }

    public string? ShardPath(string jobName)
    {
        var path = Path.Combine(_watchPath, jobName, ".audit", "audit.db");
        return File.Exists(path) ? path : null;
    }

    public void Dispose() => _refreshTimer.Dispose();
}
```

---

## B.23 — `Services/QueryRepository.cs` [NEW]

Read-only SQLite access for REST API queries. Maintains one `SqliteConnection` per shard via `ConcurrentDictionary`. All connections are opened in `ReadOnly` mode with `journal_mode=WAL` and `busy_timeout=3000` so REST reads never block the writer.

```csharp
namespace FalconAuditWebServer.Services;

using System.Collections.Concurrent;
using FalconAuditWebServer.Models;
using Microsoft.Data.Sqlite;

public class QueryRepository : IDisposable
{
    private readonly ConcurrentDictionary<string, SqliteConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly JobDiscoveryService _discovery;
    private readonly ILogger<QueryRepository> _logger;

    public QueryRepository(JobDiscoveryService discovery, ILogger<QueryRepository> logger)
    { _discovery=discovery; _logger=logger; }

    private SqliteConnection GetConnection(string dbPath)
    {
        return _connections.GetOrAdd(dbPath, path =>
        {
            try
            {
                var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
                conn.Open();
                using var p = conn.CreateCommand();
                p.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000;";
                p.ExecuteNonQuery();
                return conn;
            }
            catch (Exception ex) { _logger.LogError(ex, "QueryRepository: cannot open {P}", path); throw; }
        });
    }

    public List<JobSummary> ListJobs()
    {
        var result = new List<JobSummary>();
        foreach (var job in _discovery.KnownJobs)
        {
            var shardPath = _discovery.ShardPath(job);
            if (shardPath is null) continue;
            try
            {
                var conn = GetConnection(shardPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*), MIN(changed_at), MAX(changed_at), GROUP_CONCAT(DISTINCT machine_name) FROM audit_log";
                using var r = cmd.ExecuteReader();
                if (r.Read())
                    result.Add(new JobSummary
                    {
                        JobName        = job,
                        ShardPath      = shardPath,
                        TotalEvents    = r.IsDBNull(0) ? 0 : r.GetInt64(0),
                        FirstEvent     = r.IsDBNull(1) ? "" : r.GetString(1),
                        LastEvent      = r.IsDBNull(2) ? "" : r.GetString(2),
                        Machines       = r.IsDBNull(3) ? "" : r.GetString(3),
                        ShardSizeBytes = new FileInfo(shardPath).Length
                    });
            }
            catch (Exception ex) { _logger.LogWarning(ex, "QueryRepository: stats failed for {J}", job); }
        }
        return result;
    }

    public (List<AuditEventSummary> Items, long Total) GetEvents(string jobName, EventFilter f)
    {
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return (new(), 0);
        return GetEventsFromDb(shardPath, f);
    }

    public (List<AuditEventSummary> Items, long Total) GetEventsFromDb(string dbPath, EventFilter f)
    {
        if (!File.Exists(dbPath)) return (new(), 0);

        var conn   = GetConnection(dbPath);
        var where  = BuildWhere(f);
        var order  = f.Sort == "asc" ? "ASC" : "DESC";
        int offset = (f.Page - 1) * f.PageSize;

        long total = 0;
        using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM audit_log WHERE {where}";
            BindFilter(cnt, f);
            total = (long)(cnt.ExecuteScalar() ?? 0L);
        }

        var items = new List<AuditEventSummary>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT id,changed_at,event_type,filepath,rel_filepath,module,
            owner_service,monitor_priority,machine_name,sha256_hash,file_description,change_summary,is_backfill
            FROM audit_log WHERE {where} ORDER BY changed_at {order} LIMIT @ps OFFSET @off";
        BindFilter(cmd, f);
        cmd.Parameters.AddWithValue("@ps",  f.PageSize);
        cmd.Parameters.AddWithValue("@off", offset);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new AuditEventSummary
            {
                Id=r.GetInt64(0), ChangedAt=r.GetString(1), EventType=r.GetString(2),
                Filepath=r.GetString(3), RelFilepath=r.GetString(4), Module=r.GetString(5),
                OwnerService=r.GetString(6), MonitorPriority=r.GetString(7), MachineName=r.GetString(8),
                Sha256Hash=r.GetString(9),
                FileDescription=r.IsDBNull(10)?"":r.GetString(10),
                ChangeSummary=r.IsDBNull(11)?"":r.GetString(11),
                IsBackfill=!r.IsDBNull(12) && r.GetInt32(12)==1
            });
        return (items, total);
    }

    public AuditEventDetail? GetEvent(string jobName, long id)
    {
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return null;
        var conn = GetConnection(shardPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id,changed_at,event_type,filepath,rel_filepath,module,
            owner_service,monitor_priority,machine_name,sha256_hash,file_description,change_summary,
            old_content,diff_text,old_filepath,is_backfill
            FROM audit_log WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new AuditEventDetail
        {
            Id=r.GetInt64(0), ChangedAt=r.GetString(1), EventType=r.GetString(2),
            Filepath=r.GetString(3), RelFilepath=r.GetString(4), Module=r.GetString(5),
            OwnerService=r.GetString(6), MonitorPriority=r.GetString(7), MachineName=r.GetString(8),
            Sha256Hash=r.GetString(9),
            FileDescription=r.IsDBNull(10)?"":r.GetString(10),
            ChangeSummary=r.IsDBNull(11)?"":r.GetString(11),
            OldContent=r.IsDBNull(12)?null:r.GetString(12),
            DiffText=r.IsDBNull(13)?null:r.GetString(13),
            OldFilepath=r.IsDBNull(14)?null:r.GetString(14),
            IsBackfill=!r.IsDBNull(15) && r.GetInt32(15)==1
        };
    }

    public List<FileHistoryItem> GetFileHistory(string jobName, string relFilepath)
    {
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return new();
        var conn = GetConnection(shardPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id,changed_at,event_type,machine_name,sha256_hash,
            old_content,diff_text,is_backfill
            FROM audit_log WHERE rel_filepath=@p ORDER BY changed_at ASC";
        cmd.Parameters.AddWithValue("@p", relFilepath);
        var result = new List<FileHistoryItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new FileHistoryItem
            {
                Id=r.GetInt64(0), ChangedAt=r.GetString(1), EventType=r.GetString(2),
                MachineName=r.GetString(3), Sha256Hash=r.GetString(4),
                OldContent=r.IsDBNull(5)?null:r.GetString(5),
                DiffText=r.IsDBNull(6)?null:r.GetString(6),
                IsBackfill=!r.IsDBNull(7) && r.GetInt32(7)==1
            });
        return result;
    }

    private static string BuildWhere(EventFilter f)
    {
        var clauses = new List<string> { "1=1" };
        if (f.Module    is not null) clauses.Add("module            = @module");
        if (f.Priority  is not null) clauses.Add("monitor_priority  = @priority");
        if (f.Service   is not null) clauses.Add("owner_service     = @service");
        if (f.EventType is not null) clauses.Add("event_type        = @type");
        if (f.Machine   is not null) clauses.Add("machine_name      = @machine");
        if (f.From      is not null) clauses.Add("changed_at       >= @from");
        if (f.To        is not null) clauses.Add("changed_at       <= @to");
        if (f.Path      is not null) clauses.Add("instr(filepath, @path) > 0");
        return string.Join(" AND ", clauses);
    }

    private static void BindFilter(SqliteCommand cmd, EventFilter f)
    {
        if (f.Module    is not null) cmd.Parameters.AddWithValue("@module",   f.Module);
        if (f.Priority  is not null) cmd.Parameters.AddWithValue("@priority", f.Priority);
        if (f.Service   is not null) cmd.Parameters.AddWithValue("@service",  f.Service);
        if (f.EventType is not null) cmd.Parameters.AddWithValue("@type",     f.EventType);
        if (f.Machine   is not null) cmd.Parameters.AddWithValue("@machine",  f.Machine);
        if (f.From      is not null) cmd.Parameters.AddWithValue("@from",     f.From);
        if (f.To        is not null) cmd.Parameters.AddWithValue("@to",       f.To);
        if (f.Path      is not null) cmd.Parameters.AddWithValue("@path",     f.Path);
    }

    public void Dispose()
    {
        foreach (var c in _connections.Values) c.Dispose();
    }
}
```

---

## B.24 — `Endpoints/JobsEndpoints.cs` [NEW]

`GET /api/jobs` — list all discovered jobs with event counts, date range, and machines.
`GET /api/jobs/{jobName}/manifest` — return raw `manifest.json` for a job (chain-of-custody log).

```csharp
namespace FalconAuditWebServer.Endpoints;

using FalconAuditWebServer.Services;

public static class JobsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/jobs", (QueryRepository repo) =>
            Results.Ok(repo.ListJobs()));

        api.MapGet("/jobs/{jobName}/manifest", (string jobName, JobDiscoveryService discovery) =>
        {
            var manifestPath = Path.Combine(discovery.WatchPath, jobName, ".audit", "manifest.json");
            if (!File.Exists(manifestPath)) return Results.NotFound();
            try
            {
                var json = File.ReadAllText(manifestPath);
                return Results.Content(json, "application/json");
            }
            catch { return Results.StatusCode(500); }
        });
    }
}
```

---

## B.25 — `Endpoints/EventsEndpoints.cs` [NEW]

`GET /api/jobs/{jobName}/events` — paginated event list with filters: `module`, `priority`, `service`, `eventType`, `machine`, `from`, `to`, `path`, `page`, `pageSize` (max 500), `sort` (asc/desc).
`GET /api/jobs/{jobName}/events/{id}` — full event detail including `old_content` and `diff_text`; requires `AuditorOnly` role.
`GET /api/global/events` — paginated events from `global.db` (files directly under `c:\job\`).

```csharp
namespace FalconAuditWebServer.Endpoints;

using FalconAuditWebServer.Models;
using FalconAuditWebServer.Services;
using Microsoft.AspNetCore.Authorization;

public static class EventsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/jobs/{jobName}/events", (
            string jobName, QueryRepository repo,
            string? module, string? priority, string? service,
            string? eventType, string? machine, string? from, string? to, string? path,
            int page = 1, int pageSize = 50, string sort = "desc") =>
        {
            pageSize = Math.Min(pageSize, 500);
            var filter = new EventFilter
            {
                Module=module, Priority=priority, Service=service, EventType=eventType,
                Machine=machine, From=from, To=to, Path=path, Page=page, PageSize=pageSize, Sort=sort
            };
            var (items, total) = repo.GetEvents(jobName, filter);
            return Results.Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
        });

        api.MapGet("/jobs/{jobName}/events/{id:long}", [Authorize(Policy = "AuditorOnly")]
            (string jobName, long id, QueryRepository repo) =>
        {
            var detail = repo.GetEvent(jobName, id);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        api.MapGet("/global/events", (
            QueryRepository repo, JobDiscoveryService discovery,
            int page = 1, int pageSize = 50, string sort = "desc") =>
        {
            pageSize = Math.Min(pageSize, 500);
            var filter = new EventFilter { Page=page, PageSize=pageSize, Sort=sort };
            var (items, total) = repo.GetEventsFromDb(discovery.GlobalDb, filter);
            return Results.Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
        });
    }
}
```

---

## B.26 — `Endpoints/FileHistoryEndpoints.cs` [NEW]

`GET /api/jobs/{jobName}/history/{*filePath}` — full chronological change history for a single file. Path is validated against the job root to prevent path-traversal attacks.

```csharp
namespace FalconAuditWebServer.Endpoints;

using FalconAuditWebServer.Services;

public static class FileHistoryEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/jobs/{jobName}/history/{*filePath}", (
            string jobName, string filePath, QueryRepository repo, JobDiscoveryService discovery) =>
        {
            var jobRoot = Path.Combine(discovery.WatchPath, jobName);
            var full    = Path.GetFullPath(Path.Combine(jobRoot, filePath));
            if (!full.StartsWith(jobRoot, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Invalid file path.");

            var relPath = filePath.Replace('/', '\\');
            var history = repo.GetFileHistory(jobName, relPath);
            return Results.Ok(history);
        });
    }
}
```

---

## B.27 — Web API Models [NEW]

Five read-only response records in `FalconAuditWebServer.Models` namespace. Shared service models (`AuditLogEntry`, `FileBaseline`, `MonitorConfig`, `JobManifest`) remain in `FalconAuditService.Models` and are documented in B.3–B.6.

```csharp
// Models/JobSummary.cs
namespace FalconAuditWebServer.Models;

public record JobSummary
{
    public string JobName        { get; init; } = "";
    public string ShardPath      { get; init; } = "";
    public long   TotalEvents    { get; init; }
    public string FirstEvent     { get; init; } = "";
    public string LastEvent      { get; init; } = "";
    public string Machines       { get; init; } = "";
    public long   ShardSizeBytes { get; init; }
}
```

```csharp
// Models/AuditEventSummary.cs — list view (no content/diff columns)
namespace FalconAuditWebServer.Models;

public record AuditEventSummary
{
    public long   Id              { get; init; }
    public string ChangedAt       { get; init; } = "";
    public string EventType       { get; init; } = "";
    public string Filepath        { get; init; } = "";
    public string RelFilepath     { get; init; } = "";
    public string Module          { get; init; } = "";
    public string OwnerService    { get; init; } = "";
    public string MonitorPriority { get; init; } = "";
    public string MachineName     { get; init; } = "";
    public string Sha256Hash      { get; init; } = "";
    public string FileDescription { get; init; } = "";
    public string ChangeSummary   { get; init; } = "";
    public bool   IsBackfill      { get; init; }
}
```

```csharp
// Models/AuditEventDetail.cs — full detail view (includes content/diff, requires AuditorOnly)
namespace FalconAuditWebServer.Models;

public record AuditEventDetail
{
    public long    Id              { get; init; }
    public string  ChangedAt       { get; init; } = "";
    public string  EventType       { get; init; } = "";
    public string  Filepath        { get; init; } = "";
    public string  RelFilepath     { get; init; } = "";
    public string  Module          { get; init; } = "";
    public string  OwnerService    { get; init; } = "";
    public string  MonitorPriority { get; init; } = "";
    public string  MachineName     { get; init; } = "";
    public string  Sha256Hash      { get; init; } = "";
    public string  FileDescription { get; init; } = "";
    public string  ChangeSummary   { get; init; } = "";
    public string? OldContent      { get; init; }
    public string? DiffText        { get; init; }
    public string? OldFilepath     { get; init; }
    public bool    IsBackfill      { get; init; }
}
```

```csharp
// Models/EventFilter.cs — query parameters bound from query string
namespace FalconAuditWebServer.Models;

public record EventFilter
{
    public string? Module    { get; init; }
    public string? Priority  { get; init; }
    public string? Service   { get; init; }
    public string? EventType { get; init; }
    public string? Machine   { get; init; }
    public string? From      { get; init; }
    public string? To        { get; init; }
    public string? Path      { get; init; }
    public int     Page      { get; init; } = 1;
    public int     PageSize  { get; init; } = 50;
    public string  Sort      { get; init; } = "desc";
}
```

```csharp
// Models/FileHistoryItem.cs — one entry in a file's change timeline
namespace FalconAuditWebServer.Models;

public record FileHistoryItem
{
    public long    Id          { get; init; }
    public string  ChangedAt   { get; init; } = "";
    public string  EventType   { get; init; } = "";
    public string  MachineName { get; init; } = "";
    public string  Sha256Hash  { get; init; } = "";
    public string? OldContent  { get; init; }
    public string? DiffText    { get; init; }
    public bool    IsBackfill  { get; init; }
}
```

---

## B.28 — Summary of Changes vs `04_recommended_design.md`

| File | Status | Key changes |
|---|---|---|
| `FalconAuditWebServer.csproj` | Modified | Web SDK (`Microsoft.NET.Sdk.Web`); net8; added Negotiate, WindowsServices, Serilog packages |
| `appsettings.json` | Modified | Added `Kestrel` section for port 5100 binding; `AuditService` keys aligned |
| `Models/AuditLogEntry.cs` | Unchanged | — |
| `Models/FileBaseline.cs` | Unchanged | — |
| `Models/MonitorConfig.cs` | Modified | `GlobalDbPath`, `ClassificationRulesPath` |
| `Models/JobManifest.cs` | **New** | Chain-of-custody data model |
| `ContentCache.cs` | Unchanged | — |
| `HashHelper.cs` | Unchanged | — |
| `DiffHelper.cs` | Unchanged | — |
| `FileClassifier.cs` | Modified | Load from JSON; hot-reload via FSW |
| `SqliteRepository.cs` | Modified | Constructor takes `string dbPath` |
| `ShardRegistry.cs` | **New** | Per-job repository factory/cache |
| `ManifestManager.cs` | **New** | Reads/writes `.audit\manifest.json` |
| `DirectoryWatcher.cs` | **New** | Monitors `c:\job\` depth-1 for job folders |
| `ChangeEvent.cs` | Unchanged | — |
| `FileChangeHandler.cs` | Modified | Routes to `ShardRegistry`; `GetRepo()` helper |
| `FileMonitorService.cs` | Unchanged | — |
| `CatchUpScanner.cs` | Modified | `jobPath` scope; `GetRepo()` routing; `ShardRegistry` dependency |
| `Worker.cs` | Modified | Enumerate jobs; `DirectoryWatcher` wiring; manifest on stop |
| `Program.cs` | Modified | Merged: `WebApplication.CreateBuilder` + `UseWindowsService`; registers both audit and web DI |
| `install.ps1` | Unchanged | Minor: copy `FileClassificationRules.json` on first install |
| `FileClassificationRules.json` | **New** | 69 rules derived from `JobConfigurationFileList.json` |
| `Services/JobDiscoveryService.cs` | **New** | 30 s polling discovery of job shards |
| `Services/QueryRepository.cs` | **New** | Read-only SQLite query access for REST endpoints |
| `Endpoints/JobsEndpoints.cs` | **New** | `GET /api/jobs`, `GET /api/jobs/{name}/manifest` |
| `Endpoints/EventsEndpoints.cs` | **New** | `GET /api/jobs/{name}/events`, `/events/{id}`, `/global/events` |
| `Endpoints/FileHistoryEndpoints.cs` | **New** | `GET /api/jobs/{name}/history/{*filePath}` |
| `Models/JobSummary.cs` | **New** | Job list response model |
| `Models/AuditEventSummary.cs` | **New** | Event list row model |
| `Models/AuditEventDetail.cs` | **New** | Full event detail model |
| `Models/EventFilter.cs` | **New** | Query filter parameters |
| `Models/FileHistoryItem.cs` | **New** | File history entry model |

---

## B.29 — Hashing Strategy: Current Approach, Alternatives, and SHA-256 vs MD5

### B.29.1 — Current approach

`HashHelper.ComputeSha256()` is the single entry point for all file hashing in the service.

**What it does:**
- Opens the file with `FileStream(FileShare.ReadWrite)` — allows concurrent writers (e.g. RMS still saving the file)
- Computes SHA-256 via `SHA256.Create().ComputeHash(stream)` — streams the entire file, no full read into memory
- Returns a **64-character lowercase hex string**; returns `null` on unrecoverable failure

**Retry logic:**

| Attempt | Exception | Action |
|---|---|---|
| 0–1 | `IOException` | Sleep `100 ms × (attempt + 1)`, retry |
| 2 | `IOException` | Return `null` |
| Any | Other exception | Return `null` immediately |

**Call sites:**
- `FileChangeHandler.HandleAsync()` — on every `Created` / `Changed` / `Renamed` event after debounce
- `CatchUpScanner.CoreAsync()` — for every file found on disk during reconciliation

**Storage:**
- `file_baselines.last_hash` — baseline for change detection; updated on every recorded event
- `audit_log.sha256_hash` — permanent forensic record of the file state at the time of the event

**Change detection:** plain string equality — `if (newHash == oldHash) return;`

---

### B.29.2 — Alternatives

| # | Approach | Speed | Tamper-evident | Change detection | Trade-off |
|---|---|---|---|---|---|
| **A** | SHA-256 *(current)* | Moderate | ✅ Strong | ✅ Reliable | Reads full file on every event |
| **B** | BLAKE3 | 2–4× faster | ✅ Strong | ✅ Reliable | Needs NuGet (`Blake3`); not in BCL |
| **C** | MD5 | ~2× faster | ⚠️ Weak — collisions demonstrated (2004) | ✅ Reliable | Built-in; not suitable for audit evidence |
| **D** | xxHash64 / CRC-64 | 10×+ faster | ❌ None | ✅ Reliable | Built-in (`System.IO.Hashing`); change detection only |
| **E** | `LastWriteTimeUtc` + `FileSize` pre-check → SHA-256 | Fast on no-change | ✅ Strong | ✅ Reliable | Clock skew can miss changes; requires two new `file_baselines` columns |
| **F** | `LastWriteTimeUtc` + `FileSize` only | Fastest | ❌ None | ⚠️ Fragile | Same metadata ≠ same content |
| **G** | INI-aware semantic diff | Moderate | ❌ None | ✅ Semantically rich | INI files only; whitespace-tolerant; complex to maintain |

**Constraint from REC-002:** *"The hash of each file version shall be recorded to support integrity verification."*
This rules out D (no tamper-evidence), F (no hash at all), and G (format-specific, no hash). C is too weak for forensic use. Viable options are **A**, **B**, and **E**.

---

### B.29.3 — SHA-256 vs MD5

| Property | SHA-256 *(current)* | MD5 |
|---|---|---|
| Output size | 64 hex chars (32 bytes) | 32 hex chars (16 bytes) |
| Speed | Baseline | ~2× faster |
| Collision resistance | No known collisions | Collisions publicly demonstrated (2004) |
| Tamper detection | Forensically sound | Attacker can craft a file with matching hash |
| .NET API | `SHA256.Create()` | `MD5.Create()` |
| DB column width | 64 chars TEXT | 32 chars TEXT |

**Threat analysis:**

1. **Accidental change detection** — both are equally reliable. A randomly-modified `.ini` file producing the same MD5 is astronomically unlikely in practice.

2. **Tamper evidence** — if an operator modifies a recipe file and also edits the database record, MD5 makes constructing a colliding file feasible. SHA-256 makes it computationally infeasible. This matters if the audit log is used in a dispute about who changed what and when.

3. **Speed at these file sizes** — Falcon `.ini` files are typically < 50 KB. The hash computation difference between SHA-256 and MD5 is in the range of microseconds. The dominant latencies are the 500 ms FSW debounce and the SQLite write — not the hash.

**Verdict: keep SHA-256.**

The speed gain from switching to MD5 is immeasurable at these file sizes. The security downgrade is real and permanent. If hash computation speed ever becomes a bottleneck, the correct fix is **option E** (timestamp + size pre-check before calling `ComputeSha256`) — this eliminates the full-file read entirely for unchanged files, which has orders-of-magnitude more impact than changing the algorithm.
