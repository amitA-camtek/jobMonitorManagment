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
    public string  ChangeSummary   { get; init; } = "";
    public string? DiffText        { get; init; }
    public bool    IsBackfill      { get; init; }
    public string? LoginUser       { get; init; }
}
