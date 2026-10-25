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
        var ids = DeviceIds();
        bool severalHosts = Enumerable.Range(0, ids.Length).Select(i => Pa_GetDeviceInfo(i)->HostApi).Distinct().Count() > 1;
        for (int i = 0; i < ids.Length; i++)
        {
            var info = Pa_GetDeviceInfo(i);
            // The "#2" of a second identical device shows in its name too.
            var device = new AudioDevice(ids[i], DisplayName(info, ids[i][BaseId(i).Length..], severalHosts));
            if (info->MaxInputChannels > 0) inputs.Add(device);
            if (info->MaxOutputChannels > 0) outputs.Add(device);
        }

        Inputs = inputs;
        Outputs = outputs;
        int defaultInput = Pa_GetDefaultInputDevice();
        int defaultOutput = Pa_GetDefaultOutputDevice();
        DefaultInputId = defaultInput >= 0 && defaultInput < ids.Length ? ids[defaultInput] : null;
        DefaultOutputId = defaultOutput >= 0 && defaultOutput < ids.Length ? ids[defaultOutput] : null;
    }

    private int Find(string deviceId, bool input)
    {
        var ids = DeviceIds();
        for (int i = 0; i < ids.Length; i++)
        {
            var info = Pa_GetDeviceInfo(i);
            bool fits = input ? info->MaxInputChannels > 0 : info->MaxOutputChannels > 0;
            if (fits && ids[i] == deviceId) return i;
        }

        throw new InvalidOperationException(Texts.Get("Pa_DeviceGone"));
    }

    /// <summary>
    /// The id of every device by PortAudio index; listing, defaults and opening all use this. Two
    /// identical devices (say two USB microphones of one model) report the same name, so the second
    /// and later ones get "#2", "#3"… in PortAudio's order.
    /// </summary>
    private static string[] DeviceIds()
    {
        var ids = new string[Math.Max(0, Pa_GetDeviceCount())];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ids.Length; i++)
        {
            var id = BaseId(i);
            int n = seen[id] = seen.GetValueOrDefault(id) + 1;
            ids[i] = n == 1 ? id : $"{id} #{n}";
        }

        return ids;
    }

    private static string BaseId(int index)
    {
        var info = Pa_GetDeviceInfo(index);
        return $"{Text(Pa_GetHostApiInfo(info->HostApi)->Name)}/{Text(info->Name)}";
    }

    private static string DisplayName(DeviceInfo* info, string suffix, bool withHost)
    {
        var name = Text(info->Name) + suffix;
        return withHost ? $"{name} ({Text(Pa_GetHostApiInfo(info->HostApi)->Name)})" : name;
    }
}
