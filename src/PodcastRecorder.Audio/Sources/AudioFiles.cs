using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;

namespace PodcastRecorder.Audio.Sources;

/// <summary>Opens an audio file as a seekable stream.</summary>
public delegate WaveStream AudioFileOpener(string path);

public static class AudioFiles
{
    public static IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac" };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// WAV is read directly. On Windows everything else goes through Media Foundation; on Linux
    /// and macOS MP3 is decoded in managed code (NLayer) and the rest through ffmpeg.
    /// </summary>
    public static WaveStream Open(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)) return new WaveFileReader(path);
        if (OperatingSystem.IsWindows()) return new MediaFoundationReader(path);
        if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase)) return new Mp3FileReader(path);
        return new WaveFileReader(FfmpegDecoder.ToWav(path));
    }

    /// <summary>MP3 through NLayer, which needs no native decoder.</summary>
    private sealed class Mp3FileReader : WaveStream
    {
        private readonly NLayer.MpegFile _file;

        public Mp3FileReader(string path)
        {
            _file = new NLayer.MpegFile(path);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_file.SampleRate, _file.Channels);
        }

        public override WaveFormat WaveFormat { get; }

        public override long Length => _file.Length;

        public override long Position
        {
            get => _file.Position;
            set => _file.Position = Math.Clamp(value - value % WaveFormat.BlockAlign, 0, Length);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _file.ReadSamples(buffer, offset, count - count % WaveFormat.BlockAlign);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _file.Dispose();
            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// Decodes formats without a managed decoder (FLAC, AAC, M4A, WMA) with the ffmpeg command, into a
/// cached WAV file so the result can be seeked. Each file is decoded once per change. The cache is
/// the user's own (decoded audio is as private as the source) and is trimmed after each decode.
/// </summary>
internal static class FfmpegDecoder
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private const long MaxCacheBytes = 2L * 1024 * 1024 * 1024;

    private static readonly string CacheFolder = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches")
            : Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
        "PodcastRecorder", "decoded");

    /// <summary>
    /// An app opened from Finder gets only the system PATH (/usr/bin:/bin…), not Homebrew's, so
    /// ffmpeg is looked for in Homebrew's folders too.
    /// </summary>
    private static readonly string Ffmpeg =
        new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg" }.FirstOrDefault(p => OperatingSystem.IsMacOS() && File.Exists(p))
        ?? "ffmpeg";

    public static string ToWav(string path) => ToWav(path, CacheFolder);

    internal static string ToWav(string path, string cacheFolder)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException(Texts.Get("Files_NotFound"), path);

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..24];
        var wav = Path.Combine(cacheFolder, key + ".wav");
        if (File.Exists(wav)) return wav;

        CreatePrivateFolder(cacheFolder);
        // A name of its own, so two conversions of the same file (playback and transcription) do not
        // write into one another.
        var partial = Path.Combine(cacheFolder, $"{key}.{Guid.NewGuid():N}.part");
        var start = new ProcessStartInfo(Ffmpeg)
        {
            ArgumentList = { "-v", "error", "-nostdin", "-y", "-i", info.FullName, "-vn", "-ac", "2", "-acodec", "pcm_s16le", "-f", "wav", partial },
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new NotSupportedException(
                Texts.Format("Ffmpeg_Missing", Path.GetExtension(path)), ex);
        }

        using (process)
        {
            var errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                File.Delete(partial);
                throw new InvalidDataException(Texts.Format("Ffmpeg_Failed", errors.Trim()));
            }
        }

        try
        {
            File.Move(partial, wav, overwrite: false);
        }
        catch (IOException) when (File.Exists(wav))
        {
            // The same file was decoded meanwhile; keep that copy, which may already be playing.
            File.Delete(partial);
            return wav;
        }

        Trim(cacheFolder, wav);
        return wav;
    }

    private static void CreatePrivateFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Deletes decodes unused for <see cref="MaxAge"/>, then the oldest until the cache fits in
    /// <see cref="MaxCacheBytes"/>, and leftovers of interrupted decodes. Never <paramref name="keep"/>.
    /// A file that cannot be deleted (open elsewhere) is simply skipped.
    /// </summary>
    internal static void Trim(string cacheFolder, string keep)
    {
        var now = DateTime.UtcNow;
        try
        {
            var folder = new DirectoryInfo(cacheFolder);
            foreach (var part in folder.EnumerateFiles("*.part"))
            {
                if (now - part.LastWriteTimeUtc > TimeSpan.FromDays(1)) TryDelete(part);
            }

            var wavs = folder.EnumerateFiles("*.wav")
                .Where(f => !string.Equals(f.FullName, Path.GetFullPath(keep), StringComparison.Ordinal))
                .OrderBy(LastUsed)
                .ToList();
            long total = wavs.Sum(f => f.Length) + new FileInfo(keep).Length;
            foreach (var wav in wavs)
            {
                if (now - LastUsed(wav) <= MaxAge && total <= MaxCacheBytes) break;
                if (TryDelete(wav)) total -= wav.Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static DateTime LastUsed(FileInfo file) =>
        file.LastAccessTimeUtc > file.LastWriteTimeUtc ? file.LastAccessTimeUtc : file.LastWriteTimeUtc;

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
