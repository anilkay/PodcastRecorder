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
        DeviceName = deviceName;
        _remove = remove;
        Volume = channel.Volume;
        MonitorEnabled = channel.MonitorEnabled;
        Meter = new LevelMeterViewModel(channel.Meter);
    }

    public MixerChannel Channel { get; }

    /// <summary>The strip's title in the current language (the channel's own name if <see cref="NameSource"/> is not set).</summary>
    public string Name => NameSource?.Invoke() ?? Channel.Name;

    /// <summary>Gives the title in the current language, so it follows a language switch.</summary>
    public Func<string>? NameSource { get; init; }

    /// <summary>The number of a microphone strip ("Mikrofon 2"); 0 for other channels.</summary>
    public int MicrophoneNumber { get; init; }
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
