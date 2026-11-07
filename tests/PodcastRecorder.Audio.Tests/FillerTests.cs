using PodcastRecorder.Audio.Editing;
using PodcastRecorder.Speech;
using PodcastRecorder.Speech.Fillers;

namespace PodcastRecorder.Audio.Tests;

public class FillerTests
{
    private static readonly IReadOnlySet<string> Defaults = FillerMatcher.ParseList(FillerMatcher.DefaultWords);

    [Theory]
    [InlineData("Eee...", "e")]
    [InlineData("eeeee", "e")]
    [InlineData("Iıı,", "ı")]
    [InlineData("ıııı", "ı")]
    [InlineData("Hmmm.", "hm")]
    [InlineData("mmm", "m")]
    [InlineData("şey", "şey")]
    [InlineData("…", "")]
    public void Words_are_compared_in_lower_case_with_repeated_letters_folded(string word, string normalized) =>
        Assert.Equal(normalized, FillerMatcher.Normalize(word));

    [Theory]
    [InlineData("ııı", true)]
    [InlineData("Eeee,", true)]
    [InlineData("ee", true)]
    [InlineData("hmm", true)]
    [InlineData("Ah!", true)]
    [InlineData("eski", false)]
    [InlineData("şey", false)]
    [InlineData("yani", false)]
    [InlineData("bir", false)]
    public void The_default_list_finds_hesitations_but_not_words(string word, bool filler) =>
        Assert.Equal(filler, FillerMatcher.IsFiller(word, Defaults));

    [Fact]
    public void A_users_list_may_use_commas_spaces_or_semicolons()
    {
        var list = FillerMatcher.ParseList("ııı, eee  şey;yani,,");

        Assert.Equal(new HashSet<string> { "ı", "e", "şey", "yani" }, list);
        Assert.True(FillerMatcher.IsFiller("Şey,", list));
    }

    [Fact]
    public void Each_filler_becomes_a_padded_cut_that_stays_clear_of_the_next_word()
    {
        TimedWord[] words =
        [
            Word(0.0, 0.4, "Bugün"),
            Word(0.5, 1.0, "ııı,", 0.8),
            Word(1.2, 1.6, "teknoloji"),
            Word(1.62, 2.0, "eee", 0.6),
            Word(2.01, 2.5, "haberleri"),
        ];

        var cuts = FillerMatcher.Match(words, Defaults, TimeSpan.FromMilliseconds(40));

        Assert.Equal(2, cuts.Count);
        Assert.All(cuts, c => Assert.Equal(CutKind.Filler, c.Kind));
        Assert.Equal((0.46, 1.04, "ııı", 0.8), (Round(cuts[0].Start), Round(cuts[0].End), cuts[0].Label, cuts[0].Confidence));
        // Only 20 ms before and 10 ms after are free.
        Assert.Equal((1.6, 2.01), (Round(cuts[1].Start), Round(cuts[1].End)));
    }

    [Fact]
    public void Fillers_in_a_row_become_one_cut()
    {
        TimedWord[] words = [Word(0, 0.5, "Şimdi"), Word(0.6, 1.0, "eee"), Word(1.1, 1.5, "ııı"), Word(1.6, 2.0, "konu")];

        var cut = Assert.Single(FillerMatcher.Match(words, Defaults, TimeSpan.Zero));

        Assert.Equal((0.6, 1.5, "eee ııı"), (Round(cut.Start), Round(cut.End), cut.Label));
    }

    [Fact]
    public void Fillers_far_apart_stay_separate()
    {
        TimedWord[] words = [Word(0, 0.4, "eee"), Word(2, 2.4, "ııı")];

        Assert.Equal(2, FillerMatcher.Match(words, Defaults, TimeSpan.Zero).Count);
    }

    private static TimedWord Word(double start, double end, string text, double probability = 0.9) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text, probability);

    private static double Round(TimeSpan time) => Math.Round(time.TotalSeconds, 3);
}
