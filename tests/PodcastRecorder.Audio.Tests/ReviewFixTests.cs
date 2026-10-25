using System.Runtime.Versioning;
using NAudio.Wave;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Devices;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Tests;

/// <summary>Failure paths: the secrets key file, the ffmpeg decode cache and a device that will not start.</summary>
public sealed class ReviewFixTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("podcastrecorder-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    [UnsupportedOSPlatform("windows")] // returns at once on Windows
    public void Secret_key_is_created_once_and_read_back()
    {
        if (OperatingSystem.IsWindows()) return; // DPAPI there; the key file is Linux and macOS only.
        var path = Path.Combine(_folder.FullName, "settings", "secret.key");

        var created = CreateKey(path);
        Assert.Equal(created, CreateKey(path));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")] // returns at once on Windows
    public void Secret_key_of_the_wrong_size_is_rejected_and_left_alone()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_folder.FullName, "secret.key");
        File.WriteAllBytes(path, [1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => CreateKey(path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")] // returns at once on Windows
    public void Concurrent_first_saves_agree_on_one_key()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_folder.FullName, "secret.key");

        var keys = Enumerable.Range(0, 8).AsParallel().Select(_ => CreateKey(path)).ToList();

        Assert.All(keys, key => Assert.Equal(File.ReadAllBytes(path), key));
        Assert.Single(Directory.GetFiles(_folder.FullName));
    }

    [Fact]
    public async Task Concurrent_decodes_of_one_file_give_one_valid_wav()
    {
        if (!CrossPlatformTests.HasFfmpeg()) return;
        var flac = Path.Combine(_folder.FullName, "song.flac");
        var recorder = new Recorder();
        recorder.Start(flac, new FlacEncoder());
        recorder.Write(new float[AudioFormat.SampleRate * AudioFormat.Channels]);
        await recorder.StopAsync();
        var cache = Path.Combine(_folder.FullName, "cache");

        var wavs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => FfmpegDecoder.ToWav(flac, cache))));

        Assert.Single(wavs.Distinct());
        Assert.Equal([wavs[0]], Directory.GetFiles(cache));
        using var reader = new WaveFileReader(wavs[0]);
        Assert.InRange(reader.TotalTime.TotalSeconds, 0.9, 1.1);
    }

    [Fact]
    public void Decode_cache_drops_old_files_and_leftovers_but_keeps_the_new_one()
    {
        var old = Path.Combine(_folder.FullName, "old.wav");
        var fresh = Path.Combine(_folder.FullName, "fresh.wav");
        var keep = Path.Combine(_folder.FullName, "keep.wav");
        var leftover = Path.Combine(_folder.FullName, "x.part");
        foreach (var file in new[] { old, fresh, keep, leftover }) File.WriteAllBytes(file, new byte[16]);
        var longAgo = DateTime.UtcNow.AddDays(-30);
        foreach (var file in new[] { old, keep, leftover })
        {
            File.SetLastWriteTimeUtc(file, longAgo);
            File.SetLastAccessTimeUtc(file, longAgo);
        }

        FfmpegDecoder.Trim(_folder.FullName, keep);

        Assert.Equal([fresh, keep], Directory.GetFiles(_folder.FullName).Order());
    }

    [Fact]
    public void Output_that_fails_to_start_is_released()
    {
        using var engine = new AudioEngine();
        var backend = new FailingBackend();

        Assert.Throws<InvalidOperationException>(() => engine.StartOutput(backend, "dead"));
        Assert.True(backend.Output.Disposed);
    }

    [UnsupportedOSPlatform("windows")]
    private static byte[] CreateKey(string path) => SecretKeyFile.LoadOrCreate(path);

    private sealed class FailingBackend : IAudioBackend
    {
        public FailingOutput Output { get; } = new();

        public IReadOnlyList<AudioDevice> Inputs => [];

        public IReadOnlyList<AudioDevice> Outputs => [new("dead", "Dead")];

        public string? DefaultInputId => null;

        public string? DefaultOutputId => "dead";

        public IAudioOutput OpenOutput(string deviceId, ISampleProvider source) => Output;

        public IAudioInput OpenInput(string deviceId) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class FailingOutput : IAudioOutput
    {
        public bool Disposed { get; private set; }

        public bool IsRunning => false;

        public event EventHandler<Exception?>? Stopped { add { } remove { } }

        public void Start() => throw new InvalidOperationException("device gone");

        public void Stop()
        {
        }

        public void Dispose() => Disposed = true;
    }
}
