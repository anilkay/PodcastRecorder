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
    private readonly Dictionary<MixerChannel, MicrophoneSource> _microphones = [];
    private WasapiOut? _output;

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

    /// <summary>Raised when the output device stops, with the error if it failed.</summary>
    public event EventHandler<Exception?>? OutputStopped;

    public MixerChannel AddMicrophone(MicrophoneSource microphone, string name)
    {
        var channel = new MixerChannel(name, microphone, monitorEnabled: false);
        _microphones[channel] = microphone;
        Mixer.Add(channel);
        microphone.Start();
        return channel;
    }

    public void RemoveMicrophone(MixerChannel channel)
    {
        Mixer.Remove(channel);
        if (_microphones.Remove(channel, out var microphone))
        {
            microphone.Dispose();
        }
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
