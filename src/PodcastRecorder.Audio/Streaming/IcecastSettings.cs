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

    /// <summary>Returns a short Turkish message for the first invalid field, or null.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host)) return "Sunucu adresi gerekli.";
        if (Host.Contains("://")) return "Sunucu adresini http:// olmadan yazın (ör. yayin.example.com).";
        if (Port is < 1 or > 65535) return "Port 1 ile 65535 arasında olmalı.";
        if (string.IsNullOrWhiteSpace(Mount) || !Mount.StartsWith('/') || Mount.Any(char.IsWhiteSpace))
            return "Mount / ile başlamalı ve boşluk içermemeli (ör. /canli).";
        if (string.IsNullOrEmpty(User)) return "Kullanıcı adı gerekli (Icecast'te genelde \"source\").";
        if (string.IsNullOrEmpty(Password)) return "Şifre gerekli.";
        if (!Bitrates.Contains(BitrateKbps)) return "Geçersiz bitrate.";
        return null;
    }

    /// <summary>Leaves the password out, so the settings can be shown or logged safely.</summary>
    public override string ToString() => $"{Host}:{Port}{Mount}";
}
