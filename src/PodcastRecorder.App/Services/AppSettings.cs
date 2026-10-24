using System.IO;
using System.Text.Json.Serialization;
using PodcastRecorder.Audio;

namespace PodcastRecorder.App.Services;

public sealed class AppSettings
{
    public List<MicrophoneSettings> Microphones { get; set; } = [];

    /// <summary>Single microphone from the first versions; read once and moved into <see cref="Microphones"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputDeviceId { get; set; }

    public string? OutputDeviceId { get; set; }

    public string? EncoderId { get; set; }

    public string RecordingFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Podcast Kayıtları");

    public string? BrowseFolder { get; set; }

    public double MicrophoneVolume { get; set; } = 1;

    /// <summary>Also write every channel to its own file.</summary>
    public bool RecordStems { get; set; }

    /// <summary>Bring each finished recording to -16 LUFS.</summary>
    public bool NormalizeOnStop { get; set; }

    public double MusicVolume { get; set; } = 0.8;

    public double JingleVolume { get; set; } = 0.8;

    public double MasterVolume { get; set; } = 1;

    public double MonitorVolume { get; set; } = 1;

    public bool AutoAdvance { get; set; } = true;

    public double CrossfadeSeconds { get; set; } = 3;

    public bool DuckingEnabled { get; set; } = true;

    public double DuckLevelDb { get; set; } = -12;

    public DuckingMode DuckingMode { get; set; } = DuckingMode.WhileMicrophoneLive;

    public double VoiceThresholdDb { get; set; } = -40;

    public bool LimiterEnabled { get; set; } = true;

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

public sealed class MicrophoneSettings
{
    public string DeviceId { get; set; } = "";

    public double Volume { get; set; } = 1;

    public bool MonitorEnabled { get; set; }
}

public sealed class JingleSlot
{
    public int Index { get; set; }

    public string FilePath { get; set; } = "";
}
