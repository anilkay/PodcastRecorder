using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace PodcastRecorder.App.Services;

/// <summary>
/// Encrypts small secrets so only the same user can read them: Windows DPAPI on Windows; on Linux
/// and macOS AES-GCM with a random key kept next to the settings, readable by the user only.
/// </summary>
public static class Secrets
{
    private static readonly byte[] Entropy = "PodcastRecorder.Live"u8.ToArray();

    private static readonly string KeyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PodcastRecorder", "secret.key");

    public static string Protect(string secret)
    {
        var plain = Encoding.UTF8.GetBytes(secret);
        if (OperatingSystem.IsWindows())
        {
            return Convert.ToBase64String(ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        }

        // nonce (12) | tag (16) | cipher text
        var sealedBytes = new byte[12 + 16 + plain.Length];
        var nonce = sealedBytes.AsSpan(0, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(LoadOrCreateKey(), 16);
        aes.Encrypt(nonce, plain, sealedBytes.AsSpan(28), sealedBytes.AsSpan(12, 16), Entropy);
        return Convert.ToBase64String(sealedBytes);
    }

    /// <summary>Returns null when the value is missing or was protected by another user or machine.</summary>
    public static string? Unprotect(string? protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret)) return null;
        try
        {
            var bytes = Convert.FromBase64String(protectedSecret);
            if (OperatingSystem.IsWindows())
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
            }

            if (bytes.Length < 28 || !File.Exists(KeyPath)) return null;
            var plain = new byte[bytes.Length - 28];
            using var aes = new AesGcm(LoadOrCreateKey(), 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Entropy);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static byte[] LoadOrCreateKey()
    {
        try
        {
            return SecretKeyFile.LoadOrCreate(KeyPath);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(Localization.Loc.F("Secrets_KeyDamaged", KeyPath), ex);
        }
    }
}
