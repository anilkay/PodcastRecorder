using NAudio.Wave;

namespace PodcastRecorder.Audio.Recording;

/// <summary>Uncompressed WAV, 16-bit by default or 32-bit float for intermediate files.</summary>
public sealed class WavEncoder(bool floatingPoint = false) : IAudioEncoder
{
    private WaveFileWriter? _writer;

    public string FileExtension => ".wav";

    public void Open(string path, WaveFormat inputFormat) =>
        _writer = new WaveFileWriter(path, floatingPoint
            ? WaveFormat.CreateIeeeFloatWaveFormat(inputFormat.SampleRate, inputFormat.Channels)
            : new WaveFormat(inputFormat.SampleRate, 16, inputFormat.Channels));

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
