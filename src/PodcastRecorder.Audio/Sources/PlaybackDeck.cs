using NAudio.Wave;
using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.Audio.Sources;

/// <summary>
/// Plays the playlist: one current file, plus any earlier files still fading out during a
/// crossfade. Outputs silence while idle so it can stay connected to the mixer permanently.
/// </summary>
public sealed class PlaybackDeck(AudioFileOpener? opener = null) : ISampleProvider, IDisposable
{
    private readonly AudioFileOpener _open = opener ?? AudioFiles.Open;
    private readonly Lock _gate = new();
    private readonly List<DeckVoice> _fadingOut = [];
    private DeckVoice? _current;
    private float[] _scratch = [];

    /// <summary>Raised on the audio thread when the current file plays to its end.</summary>
    public event EventHandler<string>? PlaybackEnded;

    /// <summary>
    /// Raised once per file on the audio thread when it has <see cref="CrossfadeDuration"/>
    /// left to play, so the next file can be started over its tail.
    /// </summary>
    public event EventHandler<string>? TrackEnding;

    public WaveFormat WaveFormat => AudioFormat.Mix;

    /// <summary>Overlap between consecutive files. Zero switches files instantly.</summary>
    public TimeSpan CrossfadeDuration { get; set; }

    public bool IsPlaying
    {
        get
        {
            lock (_gate) return _current is not null;
        }
    }

    public string? CurrentFile
    {
        get
        {
            lock (_gate) return _current?.Voice.Path;
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_gate) return _current?.Voice.Position ?? TimeSpan.Zero;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_gate) return _current?.Voice.Duration ?? TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Starts a file. If another file is playing and a crossfade is set, the two overlap:
    /// the old one fades out while the new one fades in.
    /// </summary>
    public void Play(string path)
    {
        var next = new DeckVoice(new AudioFileVoice(path, _open(path)));
        var crossfade = CrossfadeDuration;
        List<DeckVoice> stopped = [];
        lock (_gate)
        {
            if (_current is not null && crossfade > TimeSpan.Zero)
            {
                _current.Fade.Start(0f, crossfade);
                _fadingOut.Add(_current);
                next.Fade.Start(0f, TimeSpan.Zero);
                next.Fade.Start(1f, crossfade);
            }
            else
            {
                if (_current is not null) stopped.Add(_current);
                stopped.AddRange(_fadingOut);
                _fadingOut.Clear();
            }

            _current = next;
        }

        foreach (var voice in stopped) voice.Voice.Dispose();
    }

    public void Stop()
    {
        List<DeckVoice> stopped;
        lock (_gate)
        {
            stopped = [.. _fadingOut];
            if (_current is not null) stopped.Add(_current);
            _fadingOut.Clear();
            _current = null;
        }

        foreach (var voice in stopped) voice.Voice.Dispose();
    }

    /// <summary>Stops playback and closes the open files.</summary>
    public void Dispose() => Stop();

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        if (_scratch.Length < count)
        {
            _scratch = new float[count];
        }

        string? ended = null;
        string? ending = null;
        List<DeckVoice> finished = [];
        lock (_gate)
        {
            for (int i = _fadingOut.Count - 1; i >= 0; i--)
            {
                var voice = _fadingOut[i];
                MixInto(voice, buffer, offset, count);
                if (voice.Voice.IsFinished || (voice.Fade.IsComplete && voice.Fade.Current == 0f))
                {
                    finished.Add(voice);
                    _fadingOut.RemoveAt(i);
                }
            }

            if (_current is { } current)
            {
                MixInto(current, buffer, offset, count);
                if (current.Voice.IsFinished)
                {
                    finished.Add(current);
                    _current = null;
                    ended = current.Voice.Path;
                }
                else if (!current.EndingRaised
                         && CrossfadeDuration > TimeSpan.Zero
                         && current.Voice.Duration - current.Voice.Position <= CrossfadeDuration)
                {
                    current.EndingRaised = true;
                    ending = current.Voice.Path;
                }
            }
        }

        foreach (var voice in finished) voice.Voice.Dispose();
        if (ending is not null) TrackEnding?.Invoke(this, ending);
        if (ended is not null) PlaybackEnded?.Invoke(this, ended);
        return count;
    }

    private void MixInto(DeckVoice voice, float[] buffer, int offset, int count)
    {
        int read = voice.Voice.Read(_scratch, 0, count);
        var samples = _scratch.AsSpan(0, read);
        voice.Fade.Apply(samples, AudioFormat.Channels);
        for (int i = 0; i < read; i++) buffer[offset + i] += samples[i];
    }

    private sealed class DeckVoice(AudioFileVoice voice)
    {
        public AudioFileVoice Voice { get; } = voice;

        public Fade Fade { get; } = new();

        public bool EndingRaised { get; set; }
    }
}
