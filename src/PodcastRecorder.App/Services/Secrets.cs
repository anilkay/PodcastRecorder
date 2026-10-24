using System.Security.Cryptography;
using System.Text;

namespace PodcastRecorder.App.Services;

/// <summary>Encrypts small secrets with Windows DPAPI so only the same Windows user can read them.</summary>
public static class Secrets
{
    private static readonly byte[] Entropy = "PodcastRecorder.Live"u8.ToArray();

    public static string Protect(string secret) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>Returns null when the value is missing or was protected by another user or machine.</summary>
    public static string? Unprotect(string? protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret)) return null;
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
