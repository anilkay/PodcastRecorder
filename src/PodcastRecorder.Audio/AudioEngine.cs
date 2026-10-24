using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio;

/// <summary>
/// Wires the sources, mixer, recorder and output device together. The output device's clock
/// drives everything: each buffer it asks for is mixed, recorded and played.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private static readonly TimeSpan MicrophoneSwitchTime = TimeSpan.FromMilliseconds(30);

    private readonly Dictionary<MixerChannel, ISampleProvider> _microphones = [];
    private WasapiOut? _output;
    private bool _microphoneLive = true;
    private bool _duckingEnabled = true;
    private float _duckLevel = DecibelsToGain(-12);

    public AudioEngine(AudioFileOpener? opener = null)
    {
        Playlist = new PlaybackDeck(opener);
        Jingles = new JinglePlayer(opener);
        MusicChannel = new MixerChannel("Müzik", Playlist);
        JingleChannel = new MixerChannel("Jingle", Jingles);
        Mixer.Add(MusicChannel);
        Mixer.Add(JingleChannel);
        Bus = new OutputBus(Mixer, Recorder);
    }

    public AudioMixer Mixer { get; } = new();

    public Recorder Recorder { get; } = new();

    public PlaybackDeck Playlist { get; }

    public JinglePlayer Jingles { get; }

    public MixerChannel MusicChannel { get; }

    public MixerChannel JingleChannel { get; }

    /// <summary>The monitor output; reading from it also feeds the recorder.</summary>
    public ISampleProvider Bus { get; }

    public bool IsOutputRunning => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>
    /// The on-air switch. Off silences every microphone; on opens them and, with ducking
    /// enabled, lowers the music underneath.
    /// </summary>
    public bool IsMicrophoneLive
    {
        get => _microphoneLive;
        set
        {
            _microphoneLive = value;
            foreach (var channel in _microphones.Keys)
            {
                channel.FadeTo(value ? 1f : 0f, MicrophoneSwitchTime);
            }

            UpdateDucking();
        }
    }

    public bool DuckingEnabled
    {
        get => _duckingEnabled;
        set
        {
            _duckingEnabled = value;
            UpdateDucking();
        }
    }

    /// <summary>Music level while the microphone is live, in dB (e.g. -12).</summary>
    public double DuckLevelDb
    {
        get => 20 * Math.Log10(_duckLevel);
        set
        {
            _duckLevel = DecibelsToGain(Math.Clamp(value, -60, 0));
            UpdateDucking();
        }
    }

    /// <summary>How long the music takes to dip and to come back.</summary>
    public TimeSpan DuckFadeTime { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Raised when the output device stops, with the error if it failed.</summary>
    public event EventHandler<Exception?>? OutputStopped;

    /// <summary>
    /// Adds a microphone channel. Several can be added; the on-air switch controls them all.
    /// The engine owns the source and disposes it when the channel is removed.
    /// </summary>
    public MixerChannel AddMicrophone(ISampleProvider microphone, string name)
    {
        var channel = new MixerChannel(name, microphone, monitorEnabled: false);
        channel.FadeTo(_microphoneLive ? 1f : 0f, TimeSpan.Zero);
        _microphones[channel] = microphone;
        Mixer.Add(channel);
        if (microphone is MicrophoneSource device) device.Start();
        UpdateDucking();
        return channel;
    }

    public void RemoveMicrophone(MixerChannel channel)
    {
        Mixer.Remove(channel);
        if (_microphones.Remove(channel, out var microphone) && microphone is IDisposable disposable)
        {
            disposable.Dispose();
        }

        UpdateDucking();
    }

    public void StartOutput(MMDevice device)
    {
        StopOutput();
        var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 60);
        output.PlaybackStopped += (_, e) => OutputStopped?.Invoke(this, e.Exception);
        output.Init(new SampleToWaveProvider(Bus));
        output.Play();
        _output = output;
    }

    public void StopOutput()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
    }

    private void UpdateDucking()
    {
        bool duck = _duckingEnabled && _microphoneLive && _microphones.Count > 0;
        MusicChannel.FadeTo(duck ? _duckLevel : 1f, DuckFadeTime);
    }

    private static float DecibelsToGain(double db) => (float)Math.Pow(10, db / 20);

    public void Dispose()
    {
        StopOutput();
        Playlist.Stop();
        Jingles.StopAll();
        foreach (var channel in _microphones.Keys.ToList())
        {
            RemoveMicrophone(channel);
        }
    }
}
