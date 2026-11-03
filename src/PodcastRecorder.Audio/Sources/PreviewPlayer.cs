using NAudio.Wave;

namespace PodcastRecorder.Audio.Sources;

/// <summary>
/// Plays a clip in memory on the monitor output only, so previewing an edit is never recorded or
/// streamed. Mixed in by <see cref="Mixing.OutputBus"/> after the monitor fader.
/// </summary>
public sealed class PreviewPlayer : ISampleProvider
{
    private readonly Lock _gate = new();
    private float[]? _clip;
    private int _position;

    public WaveFormat WaveFormat => AudioFormat.Mix;

    public bool IsPlaying
    {
        get
        {
            lock (_gate) return _clip is not null;
        }
    }

    /// <summary>Raised on the audio thread when a clip plays to its end.</summary>
    public event EventHandler? Ended;

    /// <summary>Starts <paramref name="clip"/> (interleaved <see cref="AudioFormat.Mix"/>), replacing any clip playing.</summary>
    public void Play(float[] clip)
    {
        lock (_gate)
        {
            _clip = clip;
            _position = 0;
        }
    }

    public void Stop()
    {
        lock (_gate) _clip = null;
    }

    /// <summary>Fills with the clip, then silence; returns 0 when nothing is playing.</summary>
    public int Read(float[] buffer, int offset, int count)
    {
        bool ended = false;
        int copied;
        lock (_gate)
        {
            if (_clip is null) return 0;
            copied = Math.Min(count, _clip.Length - _position);
            Array.Copy(_clip, _position, buffer, offset, copied);
            _position += copied;
            if (_position >= _clip.Length)
            {
                _clip = null;
                ended = true;
            }
        }

        Array.Clear(buffer, offset + copied, count - copied);
        if (ended) Ended?.Invoke(this, EventArgs.Empty);
        return count;
    }
}
