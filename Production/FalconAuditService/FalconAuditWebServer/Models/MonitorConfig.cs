namespace FalconAuditService.Models;

public class MonitorConfig
{
    public string WatchPath                  { get; set; } = @"C:\job\";
    public string GlobalDbPath               { get; set; } = @"C:\bis\auditlog\global.db";
    public string ClassificationRulesPath    { get; set; } = @"C:\bis\auditlog\FileClassificationRules.json";
    public string ParameterDescriptionsPath  { get; set; } = @"C:\bis\auditlog\ParameterDescriptions.json";
    public int    ApiPort                    { get; set; } = 5100;
    public string ApiBindAddress             { get; set; } = "127.0.0.1";
    public int    DebounceMs                 { get; set; } = 500;
    public int    FswBufferBytes             { get; set; } = 65_536;
    public long   MaxContentBytes            { get; set; } = 1_048_576;
    public bool   CaptureContent             { get; set; } = true;
    public int    CatchUpYieldThreshold      { get; set; } = 50;
    public int    RecoveryDelayMs            { get; set; } = 30_000;  // delay before full re-hash after FSW overflow
    public string MachineName                { get; set; } = Environment.MachineName;
}
