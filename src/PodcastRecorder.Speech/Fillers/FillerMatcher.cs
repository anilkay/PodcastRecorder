using System.Globalization;
using System.Text;
using PodcastRecorder.Audio.Editing;

namespace PodcastRecorder.Speech.Fillers;

/// <summary>Picks the filler sounds out of a word-timed transcript.</summary>
public static class FillerMatcher
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Turkish hesitation sounds; "şey" and "yani" are left to the user, as they are often real words.</summary>
    public static IReadOnlyList<string> DefaultWords { get; } = ["ııı", "ıı", "eee", "ee", "ımm", "hmm", "hımm", "mmm", "ah", "eh", "ööö"];

    public static TimeSpan DefaultPadding { get; } = TimeSpan.FromMilliseconds(40);

    /// <summary>Fillers next to each other closer than this become one cut.</summary>
    public static TimeSpan JoinGap { get; } = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// The form words are compared in: Turkish lower case, letters only, each run of one letter
    /// written once. "Eee...", "ee" and "eeeee" all become "e"; "Hmmm," becomes "hm".
    /// </summary>
    public static string Normalize(string word)
    {
        var text = new StringBuilder(word.Length);
        foreach (char c in word.ToLower(Turkish))
        {
            if (!char.IsLetter(c)) continue;
            if (text.Length > 0 && text[^1] == c) continue;
            text.Append(c);
        }

        return text.ToString();
    }

    /// <summary>The fillers in a user's list ("ııı, eee hmm; ah"), normalized.</summary>
    public static IReadOnlySet<string> ParseList(string list) =>
        list.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(w => w.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> ParseList(IEnumerable<string> words) => ParseList(string.Join(',', words));

    public static bool IsFiller(string word, IReadOnlySet<string> fillers) =>
        Normalize(word) is { Length: > 0 } normalized && fillers.Contains(normalized);

    /// <summary>
    /// One cut per run of fillers, widened by <paramref name="padding"/> on each side to cover the
    /// inexact word times, but never into the words around it.
    /// </summary>
    public static IReadOnlyList<CutRegion> Match(IReadOnlyList<TimedWord> words, IReadOnlySet<string> fillers, TimeSpan? padding = null)
    {
        var pad = padding ?? DefaultPadding;
        var regions = new List<CutRegion>();
        int i = 0;
        while (i < words.Count)
        {
            if (!IsFiller(words[i].Text, fillers))
            {
                i++;
                continue;
            }

            int first = i;
            while (i + 1 < words.Count && IsFiller(words[i + 1].Text, fillers) && words[i + 1].Start - words[i].End <= JoinGap) i++;
            int last = i++;

            var run = words.Skip(first).Take(last - first + 1).ToList();
            var region = new CutRegion(
                run[0].Start,
                run[^1].End,
                CutKind.Filler,
                string.Join(' ', run.Select(w => w.Text.Trim(' ', ',', '.', '…', '!', '?'))),
                run.Average(w => w.Probability));
            var earliest = first > 0 ? words[first - 1].End : TimeSpan.Zero;
            var latest = last + 1 < words.Count ? words[last + 1].Start : TimeSpan.MaxValue;
            regions.Add(CutList.Pad(region, pad, pad, earliest, latest));
        }

        return regions;
    }
}
