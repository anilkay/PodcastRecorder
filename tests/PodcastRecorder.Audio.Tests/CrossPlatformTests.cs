using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Tests;

/// <summary>
/// Encoding and decoding on every platform: MP3 goes through LAME and reads back through Media
/// Foundation (Windows) or NLayer; FLAC reads back through Media Foundation or ffmpeg.
/// </summary>
public sealed class CrossPlatformTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("podcastrecorder-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public Task Mp3_recording_plays_back() => RecordsAndPlaysBack(new Mp3Encoder(192));

    [Fact]
    public Task Flac_recording_plays_back()
    {
        // Linux and macOS decode FLAC with ffmpeg; skip where it is not installed.
        return OperatingSystem.IsWindows() || HasFfmpeg() ? RecordsAndPlaysBack(new FlacEncoder()) : Task.CompletedTask;
    }

    private async Task RecordsAndPlaysBack(IAudioEncoder encoder)
    {
        var path = Path.Combine(_folder.FullName, "episode" + encoder.FileExtension);
        var recorder = new Recorder();
        recorder.Start(path, encoder);
        var second = new float[AudioFormat.SampleRate * AudioFormat.Channels];
        for (int i = 0; i < second.Length; i += 2)
        {
            second[i] = second[i + 1] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * (i / 2) / AudioFormat.SampleRate);
        }

        recorder.Write(second);
        recorder.Write(second);
        await recorder.StopAsync();

        using var voice = new AudioFileVoice(path, AudioFiles.Open(path));
        Assert.InRange(voice.Duration.TotalSeconds, 1.9, 2.2);
        var buffer = new float[AudioFormat.SampleRate * AudioFormat.Channels];
        int read = voice.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, read);
        float peak = buffer.Max(MathF.Abs);
        Assert.InRange(peak, 0.4f, 0.6f);
    }

    internal static bool HasFfmpeg()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            process?.StandardOutput.ReadToEnd();
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
