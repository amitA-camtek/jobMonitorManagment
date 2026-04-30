namespace FalconAuditService;

public class LoginReader
{
    private const string LoginFilePath = @"C:\bis\data\lastLogin.json";

    public string? GetCurrentUser()
    {
        try
        {
            var json = File.ReadAllText(LoginFilePath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("Name").GetString();
        }
        catch
        {
            return null;
        }
    }
}
