using NAudio.Wave;
using PodcastRecorder.Audio;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

/// <summary>An endless source that outputs the same sample value.</summary>
internal sealed class ConstantSource(float value, WaveFormat? format = null) : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = format ?? AudioFormat.Mix;

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Fill(buffer, value, offset, count);
        return count;
    }
}

internal sealed class CapturingEncoder : IAudioEncoder
{
    public List<float> Samples { get; } = [];

    public string? Path { get; private set; }

    public bool Disposed { get; private set; }

    public string FileExtension => ".raw";

    public void Open(string path, WaveFormat inputFormat) => Path = path;

    public void Write(float[] samples, int offset, int count)
    {
        lock (Samples) Samples.AddRange(samples.AsSpan(offset, count));
    }

    public void Dispose() => Disposed = true;
}

internal sealed class TempWav : IDisposable
{
    private TempWav(string path) => Path = path;

    public string Path { get; }

    /// <summary>Writes a mono 16-bit file filled with one sample value.</summary>
    public static TempWav Create(TimeSpan length, float value = 0.5f, int sampleRate = 44100)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"podcastrecorder-{Guid.NewGuid():N}.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1)))
        {
            var samples = new float[(int)(sampleRate * length.TotalSeconds)];
            Array.Fill(samples, value);
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return new TempWav(path);
    }

    public void Dispose() => File.Delete(Path);
}
