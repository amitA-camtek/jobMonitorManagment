namespace FalconAuditWebServer.Models;

public record JobSummary
{
    public string  JobName        { get; init; } = "";
    public string  ShardPath      { get; init; } = "";
    public long    TotalEvents    { get; init; }
    public string  FirstEvent     { get; init; } = "";
    public string  LastEvent      { get; init; } = "";
    public string  Machines       { get; init; } = "";
    public long    ShardSizeBytes { get; init; }
    public string? Origin         { get; init; }   // "NewLocal" | "CopiedFromRemote" | "Unknown" | null
    public string? JobCreatedAt   { get; init; }   // ISO-8601 UTC — when the service first detected this job folder
}
