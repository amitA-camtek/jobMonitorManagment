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
