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
