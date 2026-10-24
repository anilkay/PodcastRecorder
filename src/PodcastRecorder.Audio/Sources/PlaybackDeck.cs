using NAudio.Wave;

namespace PodcastRecorder.Audio.Sources;

/// <summary>
/// Plays one file at a time (the playlist deck). Outputs silence while idle so it can stay
/// connected to the mixer permanently.
/// </summary>
public sealed class PlaybackDeck(AudioFileOpener? opener = null) : ISampleProvider
{
    private readonly AudioFileOpener _open = opener ?? AudioFiles.Open;
    private readonly Lock _gate = new();
    private AudioFileVoice? _voice;

    /// <summary>Raised on the audio thread when a file plays to its end.</summary>
    public event EventHandler<string>? PlaybackEnded;

    public WaveFormat WaveFormat => AudioFormat.Mix;

    public bool IsPlaying
    {
        get
        {
            lock (_gate) return _voice is not null;
        }
    }

    public string? CurrentFile
    {
        get
        {
            lock (_gate) return _voice?.Path;
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_gate) return _voice?.Position ?? TimeSpan.Zero;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_gate) return _voice?.Duration ?? TimeSpan.Zero;
        }
    }

    public void Play(string path)
    {
        var voice = new AudioFileVoice(path, _open(path));
        AudioFileVoice? previous;
        lock (_gate)
        {
            previous = _voice;
            _voice = voice;
        }

        previous?.Dispose();
    }

    public void Stop()
    {
        AudioFileVoice? previous;
        lock (_gate)
        {
            previous = _voice;
            _voice = null;
        }

        previous?.Dispose();
    }

    public int Read(float[] buffer, int offset, int count)
    {
        AudioFileVoice? finished = null;
        int read = 0;
        lock (_gate)
        {
            if (_voice is not null)
            {
                read = _voice.Read(buffer, offset, count);
                if (_voice.IsFinished)
                {
                    finished = _voice;
                    _voice = null;
                }
            }
        }

        Array.Clear(buffer, offset + read, count - read);

        if (finished is not null)
        {
            finished.Dispose();
            PlaybackEnded?.Invoke(this, finished.Path);
        }

        return count;
    }
}
