using NAudio.Wave;
using PodcastRecorder.Audio.Devices;
using static PodcastRecorder.Audio.PortAudio.PortAudioNative;

namespace PodcastRecorder.Audio.PortAudio;

/// <summary>
/// Sound devices on Linux (ALSA, PulseAudio and PipeWire through ALSA) and macOS (Core Audio).
/// PortAudio numbers devices by position, so ids are "host API/device name", which survive a restart.
/// </summary>
public sealed unsafe class PortAudioBackend : IAudioBackend
{
    private bool _disposed;

    public PortAudioBackend()
    {
        Check(Pa_Initialize(), Texts.Get("Pa_InitFailed"));
        Refresh();
    }

    public IReadOnlyList<AudioDevice> Inputs { get; private set; } = [];

    public IReadOnlyList<AudioDevice> Outputs { get; private set; } = [];

    public string? DefaultInputId { get; private set; }

    public string? DefaultOutputId { get; private set; }

    public IAudioOutput OpenOutput(string deviceId, ISampleProvider source) =>
        new PortAudioOutput(Find(deviceId, input: false), source);

    public IAudioInput OpenInput(string deviceId)
    {
        int index = Find(deviceId, input: true);
        return new PortAudioInput(index, deviceId, Inputs.First(d => d.Id == deviceId).Name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Pa_Terminate();
    }

    private void Refresh()
    {
        var inputs = new List<AudioDevice>();
        var outputs = new List<AudioDevice>();
        int count = Pa_GetDeviceCount();
        bool severalHosts = Enumerable.Range(0, Math.Max(0, count)).Select(i => Pa_GetDeviceInfo(i)->HostApi).Distinct().Count() > 1;
        for (int i = 0; i < count; i++)
        {
            var info = Pa_GetDeviceInfo(i);
            var device = new AudioDevice(IdOf(i), DisplayName(info, severalHosts));
            if (info->MaxInputChannels > 0) inputs.Add(device);
            if (info->MaxOutputChannels > 0) outputs.Add(device);
        }

        Inputs = inputs;
        Outputs = outputs;
        int defaultInput = Pa_GetDefaultInputDevice();
        int defaultOutput = Pa_GetDefaultOutputDevice();
        DefaultInputId = defaultInput == NoDevice ? null : IdOf(defaultInput);
        DefaultOutputId = defaultOutput == NoDevice ? null : IdOf(defaultOutput);
    }

    private int Find(string deviceId, bool input)
    {
        int count = Pa_GetDeviceCount();
        for (int i = 0; i < count; i++)
        {
            var info = Pa_GetDeviceInfo(i);
            bool fits = input ? info->MaxInputChannels > 0 : info->MaxOutputChannels > 0;
            if (fits && IdOf(i) == deviceId) return i;
        }

        throw new InvalidOperationException(Texts.Get("Pa_DeviceGone"));
    }

    private static string IdOf(int index)
    {
        var info = Pa_GetDeviceInfo(index);
        return $"{Text(Pa_GetHostApiInfo(info->HostApi)->Name)}/{Text(info->Name)}";
    }

    private static string DisplayName(DeviceInfo* info, bool withHost)
    {
        var name = Text(info->Name);
        return withHost ? $"{name} ({Text(Pa_GetHostApiInfo(info->HostApi)->Name)})" : name;
    }
}
