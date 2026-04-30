namespace FalconAuditService.Models;

public record AuditLogEntry
{
    public string  Filepath         { get; init; } = "";
    public string  RelFilepath      { get; init; } = "";   // path relative to job folder
    public string  EventType        { get; init; } = "";   // Created|Modified|Deleted|Renamed
    public string? OldContent       { get; init; }         // full text before  (P1 only)
    public string? DiffText         { get; init; }         // unified diff      (P1 Modified only)
    public string  Module           { get; init; } = "Unknown";
    public string  OwnerService     { get; init; } = "Unknown";
    public string  MonitorPriority  { get; init; } = "P3";
    public string  ChangedAt        { get; init; } = "";   // ISO-8601 UTC
    public string  MachineName      { get; init; } = "";
    public string  Sha256Hash       { get; init; } = "";
    public string  FileDescription  { get; init; } = "";   // human-readable file purpose
    public string  ChangeSummary    { get; init; } = "";   // human-readable change summary
    public bool    IsBackfill       { get; init; } = false; // true when written by CatchUpScanner
    public string? OldFilepath      { get; init; }          // populated for Renamed events only
    public string? LoginUser        { get; init; }          // user from C:\bis\data\lastLogin.json
}
