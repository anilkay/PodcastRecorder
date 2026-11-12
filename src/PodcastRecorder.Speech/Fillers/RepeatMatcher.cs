using System.Globalization;
using System.Text;
using PodcastRecorder.Audio.Editing;

namespace PodcastRecorder.Speech.Fillers;

/// <summary>
/// Finds restarts in a word-timed transcript: a phrase said twice in a row ("bugün sizinle, bugün
/// sizinle konuşacağız"), a single word said again after a hesitation ("ben, eee, ben"), and a word
/// broken off and started again ("kon- konuşacağız"). The first attempt is cut, the second stays.
/// It is deliberately cautious: a word doubled without a pause is left alone, as Turkish does that
/// on purpose ("yavaş yavaş", "güzel güzel").
/// </summary>
public static class RepeatMatcher
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>The longest phrase looked for, in words.</summary>
    public const int MaximumWords = 6;

    /// <summary>The second attempt must start within this long of the first one ending.</summary>
    public static TimeSpan MaximumGap { get; } = TimeSpan.FromSeconds(2);

    /// <summary>A single word counts as a restart only after a pause at least this long (or a filler).</summary>
    public static TimeSpan SingleWordPause { get; } = TimeSpan.FromSeconds(0.25);

    public static IReadOnlyList<CutRegion> Match(IReadOnlyList<TimedWord> words, IReadOnlySet<string> fillers, TimeSpan? padding = null)
    {
        var pad = padding ?? FillerMatcher.DefaultPadding;
        // Fillers do not break a repeat; they sit inside the cut.
        var spoken = new List<(int Index, string Key)>();
        for (int i = 0; i < words.Count; i++)
        {
            if (FillerMatcher.IsFiller(words[i].Text, fillers)) continue;
            var key = Key(words[i].Text);
            if (key.Length > 0) spoken.Add((i, key));
        }

        var regions = new List<CutRegion>();
        int at = 0;
        while (at < spoken.Count)
        {
            int length = RepeatAt(words, spoken, at);
            if (length == 0)
            {
                at++;
                continue;
            }

            int first = spoken[at].Index;
            int second = spoken[at + length].Index;
            var run = Enumerable.Range(at, length).Select(k => words[spoken[k].Index]).ToList();
            var start = words[first].Start - pad;
            var earliest = first > 0 ? words[first - 1].End : TimeSpan.Zero;
            if (start < earliest) start = earliest < words[first].Start ? earliest : words[first].Start;
            var end = words[second].Start;
            if (end > start)
            {
                regions.Add(new CutRegion(start, end, CutKind.Repeat,
                    string.Join(' ', run.Select(w => w.Text.Trim())), run.Average(w => w.Probability)));
            }

            at += length;
        }

        return regions;
    }

    /// <summary>How many words starting at <paramref name="at"/> are said again right after; 0 for none.</summary>
    private static int RepeatAt(IReadOnlyList<TimedWord> words, List<(int Index, string Key)> spoken, int at)
    {
        for (int n = Math.Min(MaximumWords, (spoken.Count - at) / 2); n >= 1; n--)
        {
            var lastOfFirst = words[spoken[at + n - 1].Index];
            var firstOfSecond = words[spoken[at + n].Index];
            var gap = firstOfSecond.Start - lastOfFirst.End;
            if (gap > MaximumGap) continue;

            bool same = true;
            for (int k = 0; k < n && same; k++) same = spoken[at + k].Key == spoken[at + n + k].Key;
            if (!same) continue;

            if (n > 1) return n;

            // One word: only with a hesitation in between, and not for one-letter words.
            bool fillerBetween = spoken[at + 1].Index - spoken[at].Index > 1;
            if (spoken[at].Key.Length >= 2 && (fillerBetween || gap >= SingleWordPause)) return 1;
        }

        // A word broken off ("kon-") and then said in full.
        if (at + 1 < spoken.Count)
        {
            var text = words[spoken[at].Index].Text.Trim();
            var next = spoken[at + 1].Key;
            var key = spoken[at].Key;
            bool brokenOff = text.EndsWith('-') || text.EndsWith('–') || text.EndsWith('—');
            if (brokenOff && next.Length > key.Length && next.StartsWith(key, StringComparison.Ordinal)
                && words[spoken[at + 1].Index].Start - words[spoken[at].Index].End <= MaximumGap)
            {
                return 1;
            }
        }

        return 0;
    }

    /// <summary>Turkish lower case, letters and digits only.</summary>
    internal static string Key(string word)
    {
        var text = new StringBuilder(word.Length);
        foreach (char c in word.ToLower(Turkish))
        {
            if (char.IsLetterOrDigit(c)) text.Append(c);
        }

        return text.ToString();
    }
}
