using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Devices;

/// <summary>Windows Audio Session API devices, through NAudio.</summary>
public sealed class WasapiBackend : IAudioBackend
{
    private readonly DeviceCatalog _catalog = new();

    public IReadOnlyList<AudioDevice> Inputs => _catalog.Inputs;

    public IReadOnlyList<AudioDevice> Outputs => _catalog.Outputs;

    public string? DefaultInputId => _catalog.DefaultInputId;

    public string? DefaultOutputId => _catalog.DefaultOutputId;

    public IAudioOutput OpenOutput(string deviceId, ISampleProvider source) =>
        new WasapiOutput(_catalog.GetDevice(deviceId), source);

    public IAudioInput OpenInput(string deviceId) => new MicrophoneSource(_catalog.GetDevice(deviceId));

    public void Dispose() => _catalog.Dispose();

    private sealed class WasapiOutput : IAudioOutput
    {
        private readonly WasapiOut _output;

        public WasapiOutput(MMDevice device, ISampleProvider source)
        {
            _output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 60);
            _output.PlaybackStopped += (_, e) => Stopped?.Invoke(this, e.Exception);
            _output.Init(new SampleToWaveProvider(source));
        }

        public bool IsRunning => _output.PlaybackState == PlaybackState.Playing;

        public event EventHandler<Exception?>? Stopped;

        public void Start() => _output.Play();

        public void Stop() => _output.Stop();

        public void Dispose() => _output.Dispose();
    }
}
