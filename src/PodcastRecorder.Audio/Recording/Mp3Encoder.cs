using NAudio.Lame;
using NAudio.Wave;

namespace PodcastRecorder.Audio.Recording;

/// <summary>MP3 via the LAME encoder bundled with NAudio.Lame.</summary>
public sealed class Mp3Encoder(int bitrateKbps) : IAudioEncoder
{
    private LameMP3FileWriter? _writer;
    private byte[] _pcm = [];

    public int BitrateKbps { get; } = bitrateKbps;

    public string FileExtension => ".mp3";

    public void Open(string path, WaveFormat inputFormat)
    {
        var pcmFormat = new WaveFormat(inputFormat.SampleRate, 16, inputFormat.Channels);
        _writer = new LameMP3FileWriter(path, pcmFormat, BitrateKbps);
    }

    public void Write(float[] samples, int offset, int count)
    {
        var writer = _writer ?? throw new InvalidOperationException("Encoder is not open.");
        int bytes = PcmConversion.ToPcm16(samples.AsSpan(offset, count), ref _pcm);
        writer.Write(_pcm, 0, bytes);
    }

    public void Dispose()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }
}
