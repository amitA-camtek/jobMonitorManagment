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
