using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace PodcastRecorder.App.Services;

/// <summary>
/// The AES key behind <see cref="Secrets"/> on Linux and macOS: 32 random bytes in a file only the
/// user can read. Kept apart from <see cref="Secrets"/> so the tests can compile it.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal static class SecretKeyFile
{
    public const int KeySize = 32;

    /// <summary>
    /// Reads the key, or creates it once. A new key is written to a temporary file and moved into
    /// place without overwriting, so two instances saving at the same time end up with one key.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file is not a key. It is left alone: replacing it would lose every saved secret.
    /// </exception>
    public static byte[] LoadOrCreate(string path)
    {
        if (File.Exists(path)) return Read(path);

        var key = RandomNumberGenerator.GetBytes(KeySize);
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        using (var file = new FileStream(temp, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        }))
        {
            file.Write(key);
        }

        try
        {
            File.Move(temp, path, overwrite: false);
            return key;
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another instance created the key first; use theirs.
            File.Delete(temp);
            return Read(path);
        }
    }

    private static byte[] Read(string path)
    {
        var key = File.ReadAllBytes(path);
        return key.Length == KeySize ? key : throw new InvalidDataException();
    }
}
