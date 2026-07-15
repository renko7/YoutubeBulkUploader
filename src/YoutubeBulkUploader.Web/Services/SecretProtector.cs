using System.Security.Cryptography;
using System.Text;

namespace YoutubeBulkUploader.Web.Services;

/// <summary>
/// Encrypts secrets (OAuth client secret, tokens) at rest using Windows DPAPI,
/// scoped to the current user account, so the SQLite file alone is not enough to read them.
/// </summary>
public class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("YoutubeBulkUploader.v1");

    public byte[] Protect(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        return ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
    }

    public string Unprotect(byte[] protectedBytes)
    {
        var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
