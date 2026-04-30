namespace FalconAuditService;

using System.Security.Cryptography;

public static class HashHelper
{
    private const int MaxRetries   = 3;
    private const int RetryDelayMs = 100;

    public static string? ComputeSha256(string path)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var fs   = new FileStream(path, FileMode.Open,
                                                FileAccess.Read, FileShare.ReadWrite);
                using var sha  = SHA256.Create();
                byte[]    hash = sha.ComputeHash(fs);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (IOException) when (attempt < MaxRetries - 1)
            {
                Thread.Sleep(RetryDelayMs * (attempt + 1));
            }
            catch (Exception)
            {
                return null;
            }
        }
        return null;
    }
}
