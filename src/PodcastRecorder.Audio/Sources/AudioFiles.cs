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
/// cached WAV file so the result can be seeked. Each file is decoded once per change.
/// </summary>
internal static class FfmpegDecoder
{
    private static readonly string CacheFolder = Path.Combine(Path.GetTempPath(), "PodcastRecorder", "decoded");

    public static string ToWav(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException(Texts.Get("Files_NotFound"), path);

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..24];
        var wav = Path.Combine(CacheFolder, key + ".wav");
        if (File.Exists(wav)) return wav;

        Directory.CreateDirectory(CacheFolder);
        var partial = wav + ".part";
        var start = new ProcessStartInfo("ffmpeg")
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

        File.Move(partial, wav, overwrite: true);
        return wav;
    }
}
