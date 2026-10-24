using CommunityToolkit.Mvvm.ComponentModel;
using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.App.ViewModels;

public partial class ChannelStripViewModel : ObservableObject
{
    public ChannelStripViewModel(MixerChannel channel)
    {
        Channel = channel;
        Name = channel.Name;
        Volume = channel.Volume;
        MonitorEnabled = channel.MonitorEnabled;
        Meter = new LevelMeterViewModel(channel.Meter);
    }

    public MixerChannel Channel { get; }

    public string Name { get; }

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
}
