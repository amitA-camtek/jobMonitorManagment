namespace FalconAuditService.Models;

public class MonitorConfig
{
    public string WatchPath                  { get; set; } = @"C:\job\";
    public string ClassificationRulesPath    { get; set; } = @"C:\bis\data\Apps\FileClassificationRules.json";
    public string ParameterDescriptionsPath  { get; set; } = @"C:\bis\data\Apps\ParameterDescriptions.json";
    public string LoginFilePath              { get; set; } = @"C:\bis\data\lastLogin.json";
    public int    ApiPort                    { get; set; } = 5100;
    public string ApiBindAddress             { get; set; } = "127.0.0.1";
    public int    DebounceMs                 { get; set; } = 500;
    public int    FswBufferBytes             { get; set; } = 65_536;
    public long   MaxContentBytes            { get; set; } = 1_048_576;
    public bool   CaptureContent             { get; set; } = true;
    public int    CatchUpYieldThreshold      { get; set; } = 50;
    public int    RecoveryDelayMs            { get; set; } = 30_000;  // delay before full re-hash after FSW overflow
    public string MachineName                { get; set; } = Environment.MachineName;

    // ── Job origin detection ────────────────────────────────────────────────
    public int    JobSettleTimeSeconds  { get; set; } = 30;   // wait after folder arrival before checking
    public int    OriginSampleSize      { get; set; } = 10;   // max P1 files to NTFS-sample
    public int    OriginDeltaMinutes    { get; set; } = 5;    // threshold: CreationTime − LastWriteTime > this → copied
    public double OriginCopiedRatio     { get; set; } = 0.6;  // fraction of sample that must exceed delta

    // ── Lazy SQLite write queue ────────────────────────────────────────────
    // The audit service does NOT keep audit.db open between writes. FSW events
    // are queued in memory; a per-job timer flushes the queue to audit.db in a
    // single transaction. Read API calls force a flush before querying so no
    // pending data is missed.
    public int FlushIntervalSeconds         { get; set; } = 1;
    public int FlushQueueMax                { get; set; } = 200;
    public int ReadConnectionTimeoutSeconds { get; set; } = 30;
}
