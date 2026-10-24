namespace PodcastRecorder.Audio.Recording;

public sealed record EncoderOption(string Id, string DisplayName, Func<IAudioEncoder> Create);

/// <summary>The output formats offered in the UI.</summary>
public static class EncoderCatalog
{
    public static IReadOnlyList<EncoderOption> All { get; } =
    [
        new("mp3-128", "MP3 128 kbps", () => new Mp3Encoder(128)),
        new("mp3-192", "MP3 192 kbps", () => new Mp3Encoder(192)),
        new("mp3-320", "MP3 320 kbps", () => new Mp3Encoder(320)),
        new("wma-128", "WMA 128 kbps", () => new WmaEncoder(128)),
        new("wma-192", "WMA 192 kbps", () => new WmaEncoder(192)),
        new("wav", "WAV 16-bit", () => new WavEncoder()),
        new("flac", "FLAC (kayıpsız)", () => new FlacEncoder()),
    ];

    public static EncoderOption Default => All[1];

    public static EncoderOption Find(string? id) => All.FirstOrDefault(o => o.Id == id) ?? Default;
}
