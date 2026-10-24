using System.IO;
using System.Text.Json.Serialization;

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

    public double CrossfadeSeconds { get; set; } = 3;

    public bool DuckingEnabled { get; set; } = true;

    public double DuckLevelDb { get; set; } = -12;

    public List<string> Playlist { get; set; } = [];

    public List<JingleBankSettings> JingleBanks { get; set; } = [];

    /// <summary>Single jingle grid from the first version; read once and moved into a bank.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<JingleSlot>? Jingles { get; set; }
}

public sealed class JingleBankSettings
{
    public string Name { get; set; } = "";

    public int Rows { get; set; } = 4;

    public int Columns { get; set; } = 4;

    public List<JingleSlot> Slots { get; set; } = [];
}

public sealed class JingleSlot
{
    public int Index { get; set; }

    public string FilePath { get; set; } = "";
}
