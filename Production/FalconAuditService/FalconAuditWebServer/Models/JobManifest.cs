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

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }   // "NewLocal" | "CopiedFromRemote" | "Unknown"

    [JsonPropertyName("originDeterminedAt")]
    public DateTimeOffset? OriginDeterminedAt { get; set; }
}

public class MachineTimestamp
{
    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("at")]
    public DateTimeOffset At { get; set; }
}

public class HistoryEntry
{
    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("from")]
    public DateTimeOffset From { get; set; }

    [JsonPropertyName("to")]
    public DateTimeOffset? To { get; set; }

    [JsonPropertyName("events")]
    public int Events { get; set; }
}
