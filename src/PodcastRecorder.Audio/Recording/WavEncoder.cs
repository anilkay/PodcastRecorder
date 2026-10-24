using NAudio.Wave;

namespace PodcastRecorder.Audio.Recording;

/// <summary>Uncompressed 16-bit WAV.</summary>
public sealed class WavEncoder : IAudioEncoder
{
    private WaveFileWriter? _writer;

    public string FileExtension => ".wav";

    public void Open(string path, WaveFormat inputFormat) =>
        _writer = new WaveFileWriter(path, new WaveFormat(inputFormat.SampleRate, 16, inputFormat.Channels));

    public void Write(float[] samples, int offset, int count)
    {
        var writer = _writer ?? throw new InvalidOperationException("Encoder is not open.");
        writer.WriteSamples(samples, offset, count);
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }
}
