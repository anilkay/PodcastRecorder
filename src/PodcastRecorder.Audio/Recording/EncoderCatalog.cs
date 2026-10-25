namespace PodcastRecorder.Audio.Recording;

/// <summary>An output format; <see cref="DisplayName"/> is in the UI language.</summary>
public sealed record EncoderOption(string Id, string DisplayNameKey, Func<IAudioEncoder> Create)
{
    public string DisplayName => Texts.Get(DisplayNameKey);
}

/// <summary>The output formats offered in the UI.</summary>
public static class EncoderCatalog
{
    public static IReadOnlyList<EncoderOption> All { get; } =
    [
        new("mp3-128", "Encoder_Mp3_128", () => new Mp3Encoder(128)),
        new("mp3-192", "Encoder_Mp3_192", () => new Mp3Encoder(192)),
        new("mp3-320", "Encoder_Mp3_320", () => new Mp3Encoder(320)),
        new("wma-128", "Encoder_Wma_128", () => new WmaEncoder(128)),
        new("wma-192", "Encoder_Wma_192", () => new WmaEncoder(192)),
        new("wav", "Encoder_Wav", () => new WavEncoder()),
        new("flac", "Encoder_Flac", () => new FlacEncoder()),
    ];

    public static EncoderOption Default => All[1];

    public static EncoderOption Find(string? id) => All.FirstOrDefault(o => o.Id == id) ?? Default;
}
