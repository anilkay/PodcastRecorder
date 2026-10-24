using NAudio.CoreAudioApi;

namespace PodcastRecorder.Audio.Devices;

public sealed record AudioDevice(string Id, string Name);

/// <summary>Lists the active WASAPI input and output devices.</summary>
public sealed class DeviceCatalog : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<AudioDevice> Inputs => List(DataFlow.Capture);

    public IReadOnlyList<AudioDevice> Outputs => List(DataFlow.Render);

    public string? DefaultInputId => DefaultId(DataFlow.Capture);

    public string? DefaultOutputId => DefaultId(DataFlow.Render);

    public MMDevice GetDevice(string id) => _enumerator.GetDevice(id);

    public void Dispose() => _enumerator.Dispose();

    private List<AudioDevice> List(DataFlow flow) =>
        _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(d => new AudioDevice(d.ID, d.FriendlyName))
            .ToList();

    private string? DefaultId(DataFlow flow) =>
        _enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)
            ? _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID
            : null;
}
