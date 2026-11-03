namespace PodcastRecorder.Audio.Editing;

/// <summary>Turns the regions picked in the editor into the stretches actually removed.</summary>
public static class CutList
{
    /// <summary>
    /// The enabled regions clamped to the recording, sorted, with overlapping ones (a filler inside
    /// a silence) and ones closer than <paramref name="joinGap"/> merged, so no sliver of audio is
    /// left between two cuts.
    /// </summary>
    public static IReadOnlyList<TimeRange> Merge(IEnumerable<CutRegion> regions, TimeSpan length, TimeSpan joinGap = default)
    {
        var merged = new List<TimeRange>();
        var sorted = regions
            .Where(r => r.Enabled)
            .Select(r => new TimeRange(Clamp(r.Start, length), Clamp(r.End, length)))
            .Where(r => r.End > r.Start)
            .OrderBy(r => r.Start);

        foreach (var range in sorted)
        {
            if (merged.Count > 0 && range.Start - merged[^1].End <= joinGap)
            {
                var last = merged[^1];
                merged[^1] = last with { End = range.End > last.End ? range.End : last.End };
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }

    /// <summary>How much shorter the recording becomes.</summary>
    public static TimeSpan TotalRemoved(IEnumerable<CutRegion> regions, TimeSpan length, TimeSpan joinGap = default) =>
        Merge(regions, length, joinGap).Aggregate(TimeSpan.Zero, (sum, r) => sum + r.Duration);

    /// <summary>
    /// Widens a region by <paramref name="before"/> and <paramref name="after"/>, but never past
    /// <paramref name="earliest"/> and <paramref name="latest"/> (the neighbouring words).
    /// </summary>
    public static CutRegion Pad(CutRegion region, TimeSpan before, TimeSpan after, TimeSpan earliest, TimeSpan latest)
    {
        var start = region.Start - before;
        var end = region.End + after;
        return region with
        {
            Start = start < earliest ? Min(earliest, region.Start) : start,
            End = end > latest ? Max(latest, region.End) : end,
        };
    }

    /// <summary>The merged cuts in sample frames at <paramref name="sampleRate"/>.</summary>
    internal static IReadOnlyList<(long Start, long End)> ToFrames(IReadOnlyList<TimeRange> cuts, int sampleRate, long totalFrames) =>
        cuts.Select(c => (Math.Clamp(ToFrame(c.Start, sampleRate), 0, totalFrames), Math.Clamp(ToFrame(c.End, sampleRate), 0, totalFrames)))
            .Where(c => c.Item2 > c.Item1)
            .ToList();

    internal static long ToFrame(TimeSpan time, int sampleRate) => (long)Math.Round(time.TotalSeconds * sampleRate);

    private static TimeSpan Clamp(TimeSpan time, TimeSpan length) =>
        time < TimeSpan.Zero ? TimeSpan.Zero : time > length ? length : time;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
