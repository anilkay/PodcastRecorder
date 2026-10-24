using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.App.ViewModels;

public partial class ChannelStripViewModel : ObservableObject
{
    private readonly Action<ChannelStripViewModel>? _remove;

    /// <param name="remove">Set for channels the user can remove, i.e. microphones.</param>
    public ChannelStripViewModel(MixerChannel channel, string? deviceName = null, Action<ChannelStripViewModel>? remove = null)
    {
        Channel = channel;
        Name = channel.Name;
        DeviceName = deviceName;
        _remove = remove;
        Volume = channel.Volume;
        MonitorEnabled = channel.MonitorEnabled;
        Meter = new LevelMeterViewModel(channel.Meter);
    }

    public MixerChannel Channel { get; }

    public string Name { get; }

    /// <summary>The input device behind a microphone strip.</summary>
    public string? DeviceName { get; }

    public string? DeviceId { get; init; }

    public bool CanRemove => _remove is not null;

    public LevelMeterViewModel Meter { get; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial bool MonitorEnabled { get; set; }

    partial void OnVolumeChanged(double value) => Channel.Volume = (float)value;

    partial void OnIsMutedChanged(bool value) => Channel.IsMuted = value;

    partial void OnMonitorEnabledChanged(bool value) => Channel.MonitorEnabled = value;

    [RelayCommand]
    private void Remove() => _remove?.Invoke(this);
}
