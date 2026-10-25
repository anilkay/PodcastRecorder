using NAudio.Wave;
using PodcastRecorder.Audio.Devices;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Sources;
using PodcastRecorder.Audio.Streaming;

namespace PodcastRecorder.Audio;

public enum DuckingMode
{
    /// <summary>Music stays down for as long as the on-air switch is on.</summary>
    WhileMicrophoneLive,

    /// <summary>Music dips only while someone is actually speaking into a live microphone.</summary>
    WhileSpeaking,
}

/// <summary>
/// Wires the sources, mixer, recorder and output device together. The output device's clock
/// drives everything: each buffer it asks for is mixed, recorded, streamed and played.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private static readonly TimeSpan MicrophoneSwitchTime = TimeSpan.FromMilliseconds(30);

    private readonly Dictionary<MixerChannel, ISampleProvider> _microphones = [];

    // Snapshot of the microphone channels for the audio thread, replaced on every change.
    private volatile MixerChannel[] _microphoneChannels = [];
    private IAudioOutput? _output;
    private bool _microphoneLive = true;
    private bool _duckingEnabled = true;
    private float _duckLevel = DecibelsToGain(-12);
    private DuckingMode _duckingMode = DuckingMode.WhileMicrophoneLive;
    private float _voiceThreshold = DecibelsToGain(-40);
    private int _voiceHoldFrames;
    private bool _voiceDucked;

    public AudioEngine(AudioFileOpener? opener = null)
    {
        Playlist = new PlaybackDeck(opener);
        Jingles = new JinglePlayer(opener);
        MusicChannel = new MixerChannel(Texts.Get("Channel_Music"), Playlist);
        JingleChannel = new MixerChannel(Texts.Get("Channel_Jingle"), Jingles);
        Mixer.Add(MusicChannel);
        Mixer.Add(JingleChannel);
        Bus = new OutputBus(Mixer, Recorder, [Live], AfterMix);
    }

    public AudioMixer Mixer { get; } = new();

    public Recorder Recorder { get; } = new();

    /// <summary>Sends the same program mix the recorder gets to an Icecast server.</summary>
    public LiveStreamer Live { get; } = new();

    public PlaybackDeck Playlist { get; }

    public JinglePlayer Jingles { get; }

    public MixerChannel MusicChannel { get; }

    public MixerChannel JingleChannel { get; }

    /// <summary>The monitor output; reading from it also feeds the recorder.</summary>
    public OutputBus Bus { get; }

    public LoudnessMeter Loudness => Bus.Loudness;

    public bool IsOutputRunning => _output?.IsRunning == true;

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
            foreach (var channel in _microphoneChannels)
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

    public DuckingMode DuckingMode
    {
        get => _duckingMode;
        set
        {
            _duckingMode = value;
            _voiceDucked = false;
            _voiceHoldFrames = 0;
            UpdateDucking();
        }
    }

    /// <summary>Microphone RMS level, in dBFS, above which speech is detected.</summary>
    public double VoiceThresholdDb
    {
        get => 20 * Math.Log10(_voiceThreshold);
        set => _voiceThreshold = DecibelsToGain(Math.Clamp(value, -70, 0));
    }

    /// <summary>How long the music stays down after the speaker stops.</summary>
    public TimeSpan VoiceHoldTime { get; set; } = TimeSpan.FromMilliseconds(700);

    /// <summary>Whether a microphone is currently above the voice threshold (with hold).</summary>
    public bool IsVoiceDetected => _voiceDucked;

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
        _microphoneChannels = [.. _microphones.Keys];
        Mixer.Add(channel);
        if (microphone is IAudioInput device) device.Start();
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

        _microphoneChannels = [.. _microphones.Keys];

        UpdateDucking();
    }

    /// <summary>Opens an output device on <paramref name="backend"/> and starts pulling the mix through it.</summary>
    public void StartOutput(IAudioBackend backend, string deviceId)
    {
        StopOutput();
        var output = backend.OpenOutput(deviceId, Bus);
        output.Stopped += (_, e) => OutputStopped?.Invoke(this, e);
        output.Start();
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
        bool trigger = _duckingMode == DuckingMode.WhileMicrophoneLive || _voiceDucked;
        bool duck = _duckingEnabled && _microphoneLive && _microphoneChannels.Length > 0 && trigger;
        float target = duck ? _duckLevel : 1f;
        if (MusicChannel.FadeTarget != target) MusicChannel.FadeTo(target, DuckFadeTime);
    }

    /// <summary>Runs on the audio thread after every block to follow the speaker.</summary>
    private void AfterMix(int frames)
    {
        if (_duckingMode != DuckingMode.WhileSpeaking) return;

        bool speaking = false;
        if (_microphoneLive)
        {
            foreach (var channel in _microphoneChannels) speaking |= channel.LastBlockRms > _voiceThreshold;
        }

        if (speaking)
        {
            _voiceHoldFrames = (int)(VoiceHoldTime.TotalSeconds * AudioFormat.SampleRate);
        }
        else
        {
            _voiceHoldFrames = Math.Max(0, _voiceHoldFrames - frames);
        }

        bool ducked = _voiceHoldFrames > 0;
        if (ducked != _voiceDucked)
        {
            _voiceDucked = ducked;
            UpdateDucking();
        }
    }

    private static float DecibelsToGain(double db) => (float)Math.Pow(10, db / 20);

    public void Dispose()
    {
        StopOutput();
        Live.StopAsync().GetAwaiter().GetResult();
        Playlist.Dispose();
        Jingles.Dispose();
        foreach (var channel in _microphones.Keys.ToList())
        {
            RemoveMicrophone(channel);
        }
    }
}
