namespace PodcastRecorder.Audio.Streaming;

/// <summary>Where and how to send the live stream. The stream is MP3 at <see cref="BitrateKbps"/>.</summary>
public sealed record IcecastSettings(
    string Host,
    int Port,
    string Mount,
    string User,
    string Password,
    int BitrateKbps,
    string? StreamName)
{
    public static IReadOnlyList<int> Bitrates { get; } = [64, 96, 128, 192, 256, 320];

    /// <summary>Returns a short message (in the UI language) for the first invalid field, or null.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host)) return Texts.Get("Icecast_NeedHost");
        if (Host.Contains("://")) return Texts.Get("Icecast_HostWithoutScheme");
        if (Port is < 1 or > 65535) return Texts.Get("Icecast_PortRange");
        if (string.IsNullOrWhiteSpace(Mount) || !Mount.StartsWith('/') || Mount.Any(char.IsWhiteSpace))
            return Texts.Get("Icecast_MountFormat");
        if (string.IsNullOrEmpty(User)) return Texts.Get("Icecast_NeedUser");
        if (string.IsNullOrEmpty(Password)) return Texts.Get("Icecast_NeedPassword");
        if (!Bitrates.Contains(BitrateKbps)) return Texts.Get("Icecast_BadBitrate");
        return null;
    }

    /// <summary>Leaves the password out, so the settings can be shown or logged safely.</summary>
    public override string ToString() => $"{Host}:{Port}{Mount}";
}
