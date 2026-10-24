using NAudio.Wave;

namespace PodcastRecorder.Audio.Sources;

/// <summary>
/// A single playing file, converted to the mix format. Reports <see cref="IsFinished"/> once
/// the file has been read to the end.
/// </summary>
public sealed class AudioFileVoice : ISampleProvider, IDisposable
{
    private readonly WaveStream _reader;
    private readonly ISampleProvider _output;

    public AudioFileVoice(string path, WaveStream reader)
    {
        Path = path;
        _reader = reader;
        _output = FormatConversion.ToMixFormat(reader.ToSampleProvider());
    }

    public string Path { get; }

    public WaveFormat WaveFormat => _output.WaveFormat;

    public TimeSpan Position => _reader.CurrentTime;

    public TimeSpan Duration => _reader.TotalTime;

    public bool IsFinished { get; private set; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (IsFinished)
        {
            return 0;
        }

        int total = 0;
        while (total < count)
        {
            int read = _output.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                IsFinished = true;
                break;
            }

            total += read;
        }

        return total;
    }

    public void Dispose() => _reader.Dispose();
}
