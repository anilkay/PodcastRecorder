using NAudio.Wave;

namespace PodcastRecorder.Audio.Sources;

/// <summary>
/// Plays jingles from the cart panel. Each pad has a key; several pads can sound at once.
/// </summary>
public sealed class JinglePlayer(AudioFileOpener? opener = null) : ISampleProvider
{
    private readonly AudioFileOpener _open = opener ?? AudioFiles.Open;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AudioFileVoice> _voices = [];
    private float[] _scratch = [];

    /// <summary>Raised on the audio thread with the pad key when a jingle plays to its end.</summary>
    public event EventHandler<string>? JingleEnded;

    public WaveFormat WaveFormat => AudioFormat.Mix;

    public bool IsPlaying(string key)
    {
        lock (_gate) return _voices.ContainsKey(key);
    }

    public void Play(string key, string path)
    {
        var voice = new AudioFileVoice(path, _open(path));
        AudioFileVoice? previous;
        lock (_gate)
        {
            _voices.Remove(key, out previous);
            _voices[key] = voice;
        }

        previous?.Dispose();
    }

    public void Stop(string key)
    {
        AudioFileVoice? previous;
        lock (_gate) _voices.Remove(key, out previous);
        previous?.Dispose();
    }

    public void StopAll()
    {
        AudioFileVoice[] voices;
        lock (_gate)
        {
            voices = [.. _voices.Values];
            _voices.Clear();
        }

        foreach (var voice in voices) voice.Dispose();
    }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        if (_scratch.Length < count)
        {
            _scratch = new float[count];
        }

        List<string>? ended = null;
        lock (_gate)
        {
            foreach (var (key, voice) in _voices)
            {
                int read = voice.Read(_scratch, 0, count);
                for (int i = 0; i < read; i++) buffer[offset + i] += _scratch[i];
                if (voice.IsFinished)
                {
                    (ended ??= []).Add(key);
                }
            }

            if (ended is not null)
            {
                foreach (var key in ended)
                {
                    _voices.Remove(key, out var voice);
                    voice?.Dispose();
                }
            }
        }

        if (ended is not null)
        {
            foreach (var key in ended) JingleEnded?.Invoke(this, key);
        }

        return count;
    }
}
