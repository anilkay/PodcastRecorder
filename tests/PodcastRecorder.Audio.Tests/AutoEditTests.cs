using NAudio.Wave;
using PodcastRecorder.Audio.Editing;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Speech;
using PodcastRecorder.Speech.Fillers;

namespace PodcastRecorder.Audio.Tests;

public class AutoEditTests
{
    private static readonly IReadOnlySet<string> Fillers = FillerMatcher.ParseList(FillerMatcher.DefaultWords);

    [Fact]
    public async Task Speech_is_cut_and_brought_to_the_podcast_loudness()
    {
        // A quiet recording with a three second pause in it.
        using var source = Synth.Tone(10, 1000, level: 0.02).Then(Synth.Silence(3)).Then(Synth.Tone(10, 1000, level: 0.02)).WriteTemp();
        using var output = new TempFile(".wav");
        CutRegion[] regions = [new(TimeSpan.FromSeconds(10.15), TimeSpan.FromSeconds(12.85), CutKind.Silence)];

        var result = await EpisodeMaster.ExportAsync(source.Path, Target(output.Path), regions, new MasterOptions());

        Assert.Equal(20.3, result.Length.TotalSeconds, 2);
        Assert.Equal(2.7, result.Removed.TotalSeconds, 2);
        Assert.Equal(TimeSpan.Zero, result.SpeechStart);
        Assert.True(result.GainDb > 10, $"gain was {result.GainDb}");
        var samples = Read(output.Path);
        Assert.Equal(20.3, samples.Length / 2.0 / AudioFormat.SampleRate, 2);
        Assert.Equal(Normalizer.PodcastTargetLufs, Loudness(samples), 0);
    }

    [Fact]
    public async Task Level_is_left_alone_when_normalizing_is_off()
    {
        using var source = Synth.Tone(5, 1000, level: 0.02).WriteTemp();
        using var output = new TempFile(".wav");

        var result = await EpisodeMaster.ExportAsync(source.Path, Target(output.Path), [], new MasterOptions { Normalize = false });

        Assert.Equal(0, result.GainDb);
        Assert.Equal(0.02f, Read(output.Path).Max(), 3);
    }

    [Fact]
    public async Task Intro_and_outro_go_around_the_speech_and_duck_under_it()
    {
        using var source = Synth.Tone(20, 1000, level: 0.1).WriteTemp();
        using var intro = Synth.Tone(10, 1100, level: 0.3).WriteTemp();
        using var outro = Synth.Tone(6, 1100, level: 0.3).WriteTemp();
        using var output = new TempFile(".wav");
        var options = new MasterOptions { IntroPath = intro.Path, OutroPath = outro.Path, Overlap = TimeSpan.FromSeconds(4) };

        var result = await EpisodeMaster.ExportAsync(source.Path, Target(output.Path), [], options);

        // 10 s intro with its last 4 s under the speech; 6 s outro with its first 3 s under it.
        Assert.Equal(6, result.SpeechStart.TotalSeconds, 3);
        Assert.Equal(6 + 20 + 3, result.Length.TotalSeconds, 2);
        var samples = Read(output.Path);
        double introAlone = Rms(samples, 1, 4);
        double speechAlone = Rms(samples, 12, 20);
        double outroAlone = Rms(samples, 27.5, 28.5);
        // The music alone plays 3 dB under the speech.
        Assert.Equal(-3, 20 * Math.Log10(introAlone / speechAlone), 0);
        Assert.Equal(-3, 20 * Math.Log10(outroAlone / speechAlone), 0);
        // Where they overlap the speech dominates: the music is ducked.
        Assert.InRange(Rms(samples, 7, 9) / speechAlone, 0.95, 1.1);
        Assert.InRange(Rms(samples, 24, 25.5) / speechAlone, 0.95, 1.1);
        Assert.True(samples.Max() <= 0.9f, "the limiter holds peaks under -1 dB");
    }

    [Fact]
    public void Timeline_without_music_is_just_the_speech()
    {
        var plan = EpisodeMaster.Timeline.Plan(48000, 0, 0, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(0.5));

        Assert.Equal(0, plan.SpeechStart);
        Assert.Equal(48000, plan.Total);
    }

    [Fact]
    public void Intro_level_falls_before_the_speech_and_ends_at_zero()
    {
        var plan = EpisodeMaster.Timeline.Plan(48000 * 60, 48000 * 10, 0, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(0.5));

        Assert.Equal(1f, plan.IntroLevel(0, 0.2f));
        Assert.Equal(1f, plan.IntroLevel(plan.SpeechStart - 48000, 0.2f));
        Assert.Equal(0.2f, plan.IntroLevel(plan.SpeechStart, 0.2f), 3);
        Assert.Equal(0.1f, plan.IntroLevel(plan.SpeechStart + 48000 * 2, 0.2f), 3);
        Assert.Equal(0f, plan.IntroLevel(48000 * 10, 0.2f));
    }

    [Fact]
    public void A_phrase_said_twice_loses_its_first_attempt()
    {
        var words = Words("Bugün", "sizinle,", "bugün", "sizinle", "konuşacağız.");

        var cut = Assert.Single(RepeatMatcher.Match(words, Fillers));

        Assert.Equal(CutKind.Repeat, cut.Kind);
        Assert.Equal(words[0].Start, cut.Start);
        Assert.Equal(words[2].Start, cut.End);
        Assert.Equal("Bugün sizinle,", cut.Label);
    }

    [Fact]
    public void A_word_said_again_after_a_filler_is_a_repeat()
    {
        var words = Words("Ben", "eee", "ben", "geldim");

        var cut = Assert.Single(RepeatMatcher.Match(words, Fillers));

        Assert.Equal(words[2].Start, cut.End);
    }

    [Fact]
    public void A_word_doubled_on_purpose_is_left_alone()
    {
        Assert.Empty(RepeatMatcher.Match(Words("Yavaş", "yavaş", "yürüdük", "ve", "güzel", "güzel", "konuştuk"), Fillers));
    }

    [Fact]
    public void A_word_said_again_after_a_pause_is_a_repeat()
    {
        var words = new List<TimedWord>
        {
            new(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.4), "Şimdi", 0.9),
            new(TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.4), "şimdi", 0.9),
            new(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), "başlıyoruz", 0.9),
        };

        var cut = Assert.Single(RepeatMatcher.Match(words, Fillers));

        Assert.Equal(TimeSpan.FromSeconds(1.0), cut.End);
    }

    [Fact]
    public void A_word_broken_off_and_started_again_is_cut()
    {
        var words = Words("Bunu", "kon-", "konuşacağız");

        var cut = Assert.Single(RepeatMatcher.Match(words, Fillers));

        Assert.Equal(words[1].Start - FillerMatcher.DefaultPadding, cut.Start);
        Assert.Equal(words[2].Start, cut.End);
    }

    [Fact]
    public void Repeats_far_apart_are_not_restarts()
    {
        var words = new List<TimedWord>
        {
            new(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.4), "çok", 0.9),
            new(TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(0.8), "güzel", 0.9),
            new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5.4), "çok", 0.9),
            new(TimeSpan.FromSeconds(5.4), TimeSpan.FromSeconds(5.8), "güzel", 0.9),
        };

        Assert.Empty(RepeatMatcher.Match(words, Fillers));
    }

    /// <summary>Words 0.4 s long with 0.1 s between them.</summary>
    private static List<TimedWord> Words(params string[] texts) =>
        texts.Select((text, i) => new TimedWord(TimeSpan.FromSeconds(i * 0.5), TimeSpan.FromSeconds(i * 0.5 + 0.4), text, 0.9)).ToList();

    private static RecordingTarget Target(string path) => new(path, new WavEncoder(floatingPoint: true));

    private static float[] Read(string path)
    {
        using var reader = new WaveFileReader(path);
        var samples = new float[reader.SampleCount * reader.WaveFormat.Channels];
        int read = reader.ToSampleProvider().Read(samples, 0, samples.Length);
        return samples[..read];
    }

    private static double Loudness(float[] samples)
    {
        var meter = new LoudnessMeter();
        meter.Process(samples, integrate: true);
        return meter.IntegratedLufs;
    }

    private static double Rms(float[] samples, double fromSeconds, double toSeconds)
    {
        int from = (int)(fromSeconds * AudioFormat.SampleRate) * 2;
        int to = (int)(toSeconds * AudioFormat.SampleRate) * 2;
        double sum = 0;
        for (int i = from; i < to; i++) sum += samples[i] * samples[i];
        return Math.Sqrt(sum / (to - from));
    }
}

public class AutoEditChapterTests
{
    [Fact]
    public async Task Chapters_move_with_the_cuts_and_the_intro()
    {
        using var source = new TempFile(".flac");
        using (var encoder = new FlacEncoder())
        {
            encoder.Open(source.Path, AudioFormat.Mix);
            var samples = Synth.Tone(30, 1000, level: 0.1).Samples;
            encoder.Write(samples, 0, samples.Length);
        }

        Chapters.Write(source.Path, [new Chapter(TimeSpan.Zero, "Giriş"), new Chapter(TimeSpan.FromSeconds(20), "Konu")]);
        using var intro = Synth.Tone(10, 1100, level: 0.3).WriteTemp();
        using var output = new TempFile(".mp3");
        CutRegion[] regions = [new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), CutKind.Silence)];

        var result = await EpisodeMaster.ExportAsync(
            source.Path, new RecordingTarget(output.Path, new Mp3Encoder(128)), regions, new MasterOptions { IntroPath = intro.Path });

        // 5 s cut before the second chapter, 6 s of intro before the speech.
        Assert.Equal(6, result.SpeechStart.TotalSeconds, 3);
        var chapters = Chapters.Read(output.Path);
        Assert.Equal(2, chapters.Count);
        Assert.Equal(TimeSpan.Zero, chapters[0].Start);
        Assert.Equal(21, chapters[1].Start.TotalSeconds, 1);
        Assert.Equal("Konu", chapters[1].Title);
    }
}
