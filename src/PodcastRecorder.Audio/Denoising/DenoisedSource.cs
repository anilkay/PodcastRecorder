using NAudio.Wave;

namespace PodcastRecorder.Audio.Denoising;

/// <summary>
/// A live source (a microphone) with RNNoise in its path, switchable while on air. On, it runs
/// 20 ms behind the microphone; off, it costs nothing and adds no delay.
/// </summary>
public sealed class DenoisedSource : ISampleProvider, IDisposable
{
    private readonly ISampleProvider _source;
    private readonly NoiseSuppressor _suppressor;
    private float[] _scratch = [];

    public DenoisedSource(ISampleProvider source, bool enabled)
    {
        if (source.WaveFormat.SampleRate != AudioFormat.SampleRate)
        {
            throw new ArgumentException($"RNNoise needs {AudioFormat.SampleRate} Hz audio.", nameof(source));
        }

        _source = source;
        _suppressor = new NoiseSuppressor(source.WaveFormat.Channels) { Enabled = enabled };
    }

    public ISampleProvider Source => _source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool Enabled
    {
        get => _suppressor.Enabled;
        set => _suppressor.Enabled = value;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_scratch.Length < count) _scratch = new float[count];
        int read = _source.Read(_scratch, 0, count);
        read -= read % WaveFormat.Channels;
        _suppressor.Process(_scratch.AsSpan(0, read), buffer.AsSpan(offset, read));
        return read;
    }

    public void Dispose() => _suppressor.Dispose();
}

/// <summary>
/// A file-length source cleaned with RNNoise, sample-aligned with the original: the 20 ms
/// processing delay is dropped at the start and flushed out at the end, so a cleaned track keeps
/// the exact length and timing of its original (and of the other tracks of the recording).
/// </summary>
public sealed class DenoisedFile : ISampleProvider, IDisposable
{
    private readonly ISampleProvider _source;
    private readonly NoiseSuppressor _suppressor;
    private readonly int _channels;
    private float[] _scratch = [];
    private int _toSkip;
    private int _flushLeft;
    private bool _sourceEnded;

    public DenoisedFile(ISampleProvider source)
    {
        if (source.WaveFormat.SampleRate != AudioFormat.SampleRate)
        {
            throw new ArgumentException($"RNNoise needs {AudioFormat.SampleRate} Hz audio.", nameof(source));
        }

        _source = source;
        _channels = source.WaveFormat.Channels;
        _suppressor = new NoiseSuppressor(_channels);
        _toSkip = NoiseSuppressor.LatencyFrames * _channels;
        _flushLeft = NoiseSuppressor.LatencyFrames * _channels;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        count -= count % _channels;
        int total = 0;
        while (total < count)
        {
            int want = count - total;
            if (_scratch.Length < want) _scratch = new float[want];

            int got;
            if (!_sourceEnded)
            {
                got = _source.Read(_scratch, 0, want);
                got -= got % _channels;
                if (got == 0)
                {
                    _sourceEnded = true;
                    continue;
                }
            }
            else
            {
                // Push silence through to get the last 20 ms of real audio out.
                got = Math.Min(want, _flushLeft);
                if (got == 0) break;
                _scratch.AsSpan(0, got).Clear();
                _flushLeft -= got;
            }

            var processed = buffer.AsSpan(offset + total, got);
            _suppressor.Process(_scratch.AsSpan(0, got), processed);

            int skip = Math.Min(_toSkip, got);
            if (skip > 0)
            {
                processed[skip..].CopyTo(processed);
                _toSkip -= skip;
                got -= skip;
            }

            total += got;
        }

        return total;
    }

    public void Dispose() => _suppressor.Dispose();
}
