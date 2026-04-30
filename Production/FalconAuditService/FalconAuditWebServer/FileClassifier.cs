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
            if (!File.Exists(configPath))
            {
                _logger.LogWarning("FileClassificationRules.json not found at {P} — using empty rule set.", configPath);
                StartConfigWatcher(configPath);
                return;
            }

            var json    = File.ReadAllText(configPath);
            var ruleset = JsonSerializer.Deserialize<RuleSet>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                                            ReadCommentHandling = JsonCommentHandling.Skip,
                                            AllowTrailingCommas = true });

            if (ruleset?.Rules is null)
            {
                _logger.LogWarning("FileClassificationRules.json has no rules.");
                StartConfigWatcher(configPath);
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

        var dir  = Path.GetDirectoryName(configPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        var file = Path.GetFileName(configPath);

        _configWatcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = System.IO.NotifyFilters.LastWrite,
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
