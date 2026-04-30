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
    private static readonly Regex _keyValueRx = new(@"^([+\- ])\s*([^=\s][^=]*)=(.*)$", RegexOptions.Compiled);

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
                                            ReadCommentHandling = JsonCommentHandling.Skip,
                                            AllowTrailingCommas = true });

            if (doc?.Files is null) { _logger.LogWarning("ChangeDescriptionEnricher: no files map in {P}.", configPath); StartWatcher(configPath); return; }

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
            NotifyFilter = System.IO.NotifyFilters.LastWrite, EnableRaisingEvents = true
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
        foreach (var (key, value) in changes)
        {
            var (oldVal, newVal) = value;
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

    // Returns dict: "Section.Key" → (oldValue, newValue). Values are "" when only one side exists.
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
