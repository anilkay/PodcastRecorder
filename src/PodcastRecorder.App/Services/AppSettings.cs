using System.IO;
using System.Text.Json.Serialization;
using PodcastRecorder.Audio;
using PodcastRecorder.Speech.Providers;
using PodcastRecorder.App.Localization;

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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Loc.T("Default_RecordingFolder"));

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

    public LiveStreamSettings Live { get; set; } = new();

    public AiSettings Ai { get; set; } = new();

    public RecordingTagsSettings Tags { get; set; } = new();

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>"system" (follow Windows), "tr" or "en".</summary>
    public string Language { get; set; } = Loc.System;
}

public sealed class JingleBankSettings
{
    public string Name { get; set; } = "";

    public int Rows { get; set; } = 4;

    public int Columns { get; set; } = 4;

    public List<JingleSlot> Slots { get; set; } = [];
}

/// <summary>Speech to text and text to speech services. Keys are encrypted for the current Windows user.</summary>
public sealed class AiSettings
{
    public string TranscriptionProvider { get; set; } = SpeechProviders.LocalWhisper;

    public string SynthesisProvider { get; set; } = SpeechProviders.Windows;

    /// <summary>ISO code such as "tr"; empty lets the service detect it.</summary>
    public string Language { get; set; } = "tr";

    public bool TranscribeOnStop { get; set; }

    public string WhisperModel { get; set; } = "base";

    public string OpenAiBaseUrl { get; set; } = OpenAiEndpoint.DefaultOpenAiUrl;

    public string? OpenAiProtectedKey { get; set; }

    public string OpenAiTranscriptionModel { get; set; } = "whisper-1";

    public string OpenAiSpeechModel { get; set; } = "gpt-4o-mini-tts";

    public string AzureOpenAiEndpoint { get; set; } = "";

    public string? AzureOpenAiProtectedKey { get; set; }

    public string AzureOpenAiTranscriptionDeployment { get; set; } = "whisper";

    public string AzureOpenAiSpeechDeployment { get; set; } = "gpt-4o-mini-tts";

    public string AzureSpeechRegion { get; set; } = "westeurope";

    public string? AzureSpeechProtectedKey { get; set; }

    /// <summary>Last voice used with each synthesis provider.</summary>
    public Dictionary<string, string> Voices { get; set; } = [];
}

/// <summary>
/// File information that stays the same from episode to episode, plus the next episode number.
/// Title, year and description are per recording and not kept.
/// </summary>
public sealed class RecordingTagsSettings
{
    public string Artist { get; set; } = "";

    public string Album { get; set; } = "";

    public string Genre { get; set; } = "Podcast";

    public string? CoverImagePath { get; set; }

    public uint NextEpisode { get; set; } = 1;
}

public sealed class MicrophoneSettings
{
    public string DeviceId { get; set; } = "";

    public double Volume { get; set; } = 1;

    public bool MonitorEnabled { get; set; }
}

/// <summary>The Icecast server to stream to. The password is kept encrypted for the Windows user.</summary>
public sealed class LiveStreamSettings
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 8000;

    public string Mount { get; set; } = Loc.T("Default_Mount");

    public string User { get; set; } = "source";

    /// <summary>DPAPI-protected password (see <see cref="Secrets"/>); never stored as plain text.</summary>
    public string? ProtectedPassword { get; set; }

    public int BitrateKbps { get; set; } = 128;

    public string StreamName { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && ProtectedPassword is not null;
}

public sealed class JingleSlot
{
    public int Index { get; set; }

    public string FilePath { get; set; } = "";
}
