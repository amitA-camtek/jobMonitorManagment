using FalconAuditService.Models;

namespace FalconAuditService;

public class LoginReader
{
    private readonly string _loginFilePath;

    public LoginReader(MonitorConfig config)
    {
        _loginFilePath = config.LoginFilePath;
    }

    public string? GetCurrentUser()
    {
        try
        {
            var json = File.ReadAllText(_loginFilePath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("Name").GetString();
        }
        catch
        {
            return null;
        }
    }
}
