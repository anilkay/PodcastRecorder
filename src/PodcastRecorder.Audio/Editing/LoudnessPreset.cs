using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Editing;

/// <summary>
/// A loudness the finished episode is brought to, as the platform it is published on expects it:
/// the integrated loudness of the speech and the level the limiter holds the peaks under.
/// </summary>
/// <param name="Id">Saved in the settings; never shown.</param>
/// <param name="TargetLufs">Integrated loudness of the speech.</param>
/// <param name="CeilingDb">Highest peak, in dB below full scale.</param>
public sealed record LoudnessPreset(string Id, double TargetLufs, double CeilingDb)
{
    public const string CustomId = "custom";

    /// <summary>The loudness a custom preset may be set to.</summary>
    public const double MinimumLufs = -24, MaximumLufs = -10;

    /// <summary>YouTube turns louder uploads down to -14 LUFS.</summary>
    public static LoudnessPreset YouTube { get; } = new("youtube", -14, -1);

    /// <summary>Spotify plays everything at -14 LUFS by default.</summary>
    public static LoudnessPreset Spotify { get; } = new("spotify", -14, -1);

    /// <summary>Apple Podcasts asks for -16 LUFS, the usual podcast loudness.</summary>
    public static LoudnessPreset ApplePodcasts { get; } = new("apple", Normalizer.PodcastTargetLufs, -1);

    /// <summary>EBU R128, radio and television in Europe.</summary>
    public static LoudnessPreset Broadcast { get; } = new("ebu-r128", -23, -1);

    /// <summary>The presets that stand for a platform, in the order they are offered.</summary>
    public static IReadOnlyList<LoudnessPreset> All { get; } = [YouTube, Spotify, ApplePodcasts, Broadcast];

    /// <summary>The preset most podcasts want, and what the automatic edit did before presets existed.</summary>
    public static LoudnessPreset Default => ApplePodcasts;

    /// <summary>A preset of one's own; the loudness is kept within <see cref="MinimumLufs"/>..<see cref="MaximumLufs"/>.</summary>
    public static LoudnessPreset Custom(double targetLufs) =>
        new(CustomId, Math.Clamp(double.IsFinite(targetLufs) ? targetLufs : Normalizer.PodcastTargetLufs, MinimumLufs, MaximumLufs), -1);

    /// <summary>The preset saved as <paramref name="id"/>; an unknown or empty id gives <see cref="Default"/>.</summary>
    public static LoudnessPreset Find(string? id, double customLufs = Normalizer.PodcastTargetLufs) =>
        id == CustomId ? Custom(customLufs) : All.FirstOrDefault(p => p.Id == id) ?? Default;

    /// <summary>
    /// The ceiling to give the limiter: the limiter looks at samples, and a lossy encoder (and the
    /// peaks between samples) can push a peak up to about half a dB higher, so lossy files get that
    /// much more room.
    /// </summary>
    public double LimiterCeilingDb(bool lossyOutput) => lossyOutput ? CeilingDb - 0.5 : CeilingDb;
}
