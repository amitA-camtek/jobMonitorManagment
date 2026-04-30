namespace FalconAuditService.Models;

public record FileBaseline
{
    public string  Filepath     { get; init; } = "";
    public string  LastHash     { get; init; } = "";
    public string  LastSeen     { get; init; } = "";   // ISO-8601 UTC
    public string? LastContent  { get; init; }         // cached content for P1 diff (may be null)
}
