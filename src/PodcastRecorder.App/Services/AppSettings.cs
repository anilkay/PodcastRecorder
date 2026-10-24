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

    public LiveStreamSettings Live { get; set; } = new();
}

/// <summary>The Icecast server to stream to. The password is kept encrypted for the Windows user.</summary>
public sealed class LiveStreamSettings
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 8000;

    public string Mount { get; set; } = "/canli";

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
