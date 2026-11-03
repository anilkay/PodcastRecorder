namespace PodcastRecorder.Audio.Editing;

public enum CutKind
{
    /// <summary>A long pause, shortened to the kept gap.</summary>
    Silence,

    /// <summary>A filler sound such as "ııı", "eee" or "hmm".</summary>
    Filler,
}

/// <summary>
/// A stretch of a recording to remove, timed from its start. <see cref="Label"/> is what was heard
/// (the filler as transcribed, or the level of a silence); <see cref="Confidence"/> runs from 0 to 1.
/// </summary>
public sealed record CutRegion(TimeSpan Start, TimeSpan End, CutKind Kind, string Label = "", double Confidence = 1, bool Enabled = true)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>A stretch of time from <see cref="Start"/> up to (not including) <see cref="End"/>.</summary>
public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}
