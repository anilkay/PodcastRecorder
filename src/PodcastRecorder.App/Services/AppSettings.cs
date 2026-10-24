using System.IO;

namespace PodcastRecorder.App.Services;

public sealed class AppSettings
{
    public string? InputDeviceId { get; set; }

    public string? OutputDeviceId { get; set; }

    public string? EncoderId { get; set; }

    public string RecordingFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Podcast Kayıtları");

    public string? BrowseFolder { get; set; }

    public double MicrophoneVolume { get; set; } = 1;

    public double MusicVolume { get; set; } = 0.8;

    public double JingleVolume { get; set; } = 0.8;

    public double MasterVolume { get; set; } = 1;

    public double MonitorVolume { get; set; } = 1;

    public bool AutoAdvance { get; set; } = true;

    public List<string> Playlist { get; set; } = [];

    public List<JingleSlot> Jingles { get; set; } = [];
}

public sealed class JingleSlot
{
    public int Index { get; set; }

    public string FilePath { get; set; } = "";
}
