using System.Diagnostics;
using NAudio.MediaFoundation;
using NAudio.Wave;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// AAC in an M4A file, the format podcast hosts and Apple Podcasts like best, and one that holds
/// chapters. Encoded with Windows Media Foundation on Windows and with ffmpeg elsewhere. Like
/// <see cref="WmaEncoder"/>, the recording goes to a WAV file first and is converted when closed;
/// if that fails the WAV file is kept, so the recording is never lost.
/// </summary>
public sealed class M4aEncoder(int bitrateKbps) : IAudioEncoder
{
    private WaveFileWriter? _writer;
    private string? _path;
    private string? _wavPath;

    public int BitrateKbps { get; } = bitrateKbps;

    public string FileExtension => ".m4a";

    public void Open(string path, WaveFormat inputFormat)
    {
        var pcmFormat = new WaveFormat(inputFormat.SampleRate, 16, inputFormat.Channels);
        // Fail at the start, not after the show, when there is nothing to encode with.
        if (OperatingSystem.IsWindows()) SelectMediaType(pcmFormat);
        else CheckFfmpeg();
        _path = path;
        _wavPath = path + ".kayit.wav";
        _writer = new WaveFileWriter(_wavPath, pcmFormat);
    }

    public void Write(float[] samples, int offset, int count)
    {
        var writer = _writer ?? throw new InvalidOperationException("Encoder is not open.");
        writer.WriteSamples(samples, offset, count);
    }

    /// <summary>Closes the temporary WAV file and converts it to M4A. Can take a while for long recordings.</summary>
    public void Dispose()
    {
        if (_writer is null || _path is null || _wavPath is null)
        {
            return;
        }

        var pcmFormat = _writer.WaveFormat;
        _writer.Dispose();
        _writer = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Media Foundation objects must stay on the thread that made them, so the type is chosen again here.
                using var reader = new WaveFileReader(_wavPath);
                using var encoder = new MediaFoundationEncoder(SelectMediaType(pcmFormat));
                encoder.Encode(_path, reader);
            }
            else
            {
                EncodeWithFfmpeg(_wavPath, _path);
            }

            File.Delete(_wavPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (File.Exists(_path)) File.Delete(_path);
            var kept = Path.ChangeExtension(_path, ".wav");
            File.Move(_wavPath, kept, overwrite: false);
            throw new IOException(Texts.Format("M4a_ConvertFailed", kept), ex);
        }
    }

    private MediaType SelectMediaType(WaveFormat pcmFormat)
    {
        MediaFoundationApi.Startup();
        return MediaFoundationEncoder.SelectMediaType(AudioSubtypes.MFAudioFormat_AAC, pcmFormat, BitrateKbps * 1000)
            ?? throw new InvalidOperationException(Texts.Get("M4a_NoEncoder"));
    }

    private static void CheckFfmpeg()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(FfmpegDecoder.Ffmpeg)
            {
                ArgumentList = { "-v", "quiet", "-version" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(Texts.Get("M4a_NoEncoder"), ex);
        }
    }

    private void EncodeWithFfmpeg(string wav, string path)
    {
        var start = new ProcessStartInfo(FfmpegDecoder.Ffmpeg)
        {
            ArgumentList =
            {
                "-v", "error", "-nostdin", "-y", "-i", wav, "-c:a", "aac", "-b:a", $"{BitrateKbps}k",
                "-movflags", "+faststart", "-f", "ipod", path,
            },
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException(Texts.Get("M4a_NoEncoder"));
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidDataException(Texts.Format("Ffmpeg_Failed", errors.Trim()));
    }
}
