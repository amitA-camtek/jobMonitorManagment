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
