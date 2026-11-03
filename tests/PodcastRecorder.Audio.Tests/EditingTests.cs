using System.Security.Cryptography;
using NAudio.Wave;
using PodcastRecorder.Audio.Editing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public class SilenceDetectorTests
{
    [Fact]
    public void A_long_pause_between_tones_is_shortened_to_the_kept_gap()
    {
        var audio = Synth.Tone(1).Then(Synth.Silence(2)).Then(Synth.Tone(1));

        var regions = SilenceDetector.Detect(audio.Provider(), new SilenceOptions());

        var region = Assert.Single(regions);
        Assert.Equal(CutKind.Silence, region.Kind);
        // 0.15 s of the pause stays on each side; the level is measured in 20 ms windows.
        Assert.Equal(1.15, region.Start.TotalSeconds, 0.021);
        Assert.Equal(2.85, region.End.TotalSeconds, 0.021);
        Assert.Equal("-120 dB", region.Label);
    }

    [Fact]
    public void Pauses_shorter_than_the_minimum_are_left_alone()
    {
        var audio = Synth.Tone(1).Then(Synth.Silence(0.5)).Then(Synth.Tone(1)).Then(Synth.Silence(1.2)).Then(Synth.Tone(0.5));

        var regions = SilenceDetector.Detect(audio.Provider(), new SilenceOptions { MinimumSilence = TimeSpan.FromSeconds(0.8) });

        var region = Assert.Single(regions);
        Assert.Equal(2.5 + 0.15, region.Start.TotalSeconds, 0.021);
    }

    [Theory]
    [InlineData(-50, true)]
    [InlineData(-30, false)]
    public void Background_noise_counts_as_silence_only_below_the_threshold(double noiseDb, bool cut)
    {
        var audio = Synth.Tone(1).Then(Synth.Noise(2, noiseDb)).Then(Synth.Tone(1));

        var regions = SilenceDetector.Detect(audio.Provider(), new SilenceOptions { ThresholdDb = -40 });

        Assert.Equal(cut, regions.Count == 1);
    }

    [Fact]
    public void The_kept_gap_is_configurable()
    {
        var audio = Synth.Tone(1).Then(Synth.Silence(2)).Then(Synth.Tone(1));

        var region = Assert.Single(SilenceDetector.Detect(audio.Provider(), new SilenceOptions { KeptGap = TimeSpan.FromSeconds(1) }));

        Assert.Equal(1.0, region.Duration.TotalSeconds, 0.041);
    }
}

public class CutListTests
{
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(60);

    [Fact]
    public void Overlapping_and_touching_regions_merge_and_disabled_ones_are_skipped()
    {
        CutRegion[] regions =
        [
            Region(10, 12, CutKind.Silence),
            Region(11.5, 12.4, CutKind.Filler),
            Region(12.4, 13, CutKind.Filler),
            Region(20, 21, CutKind.Silence, enabled: false),
            Region(30, 31, CutKind.Filler),
        ];

        var merged = CutList.Merge(regions, Length);

        Assert.Equal([new(S(10), S(13)), new(S(30), S(31))], merged);
        Assert.Equal(4, CutList.TotalRemoved(regions, Length).TotalSeconds, 6);
    }

    [Fact]
    public void Regions_are_clamped_to_the_recording_and_close_ones_joined()
    {
        CutRegion[] regions = [Region(-1, 0.5, CutKind.Silence), Region(0.51, 1, CutKind.Filler), Region(59.5, 70, CutKind.Silence)];

        var merged = CutList.Merge(regions, Length, joinGap: TimeSpan.FromMilliseconds(20));

        Assert.Equal([new(S(0), S(1)), new(S(59.5), S(60))], merged);
    }

    [Fact]
    public void Padding_widens_a_region_but_not_into_its_neighbours()
    {
        var filler = Region(5, 5.5, CutKind.Filler);

        var free = CutList.Pad(filler, S(0.05), S(0.05), S(0), S(100));
        var tight = CutList.Pad(filler, S(0.05), S(0.05), earliest: S(4.98), latest: S(5.51));

        Assert.Equal((4.95, 5.55), (free.Start.TotalSeconds, free.End.TotalSeconds));
        Assert.Equal((4.98, 5.51), (tight.Start.TotalSeconds, tight.End.TotalSeconds));
    }

    private static CutRegion Region(double start, double end, CutKind kind, bool enabled = true) =>
        new(S(start), S(end), kind, Enabled: enabled);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);
}

public class EditExporterTests
{
    private static readonly TimeSpan Crossfade = TimeSpan.FromMilliseconds(15);

    [Fact]
    public void The_edit_is_exactly_the_original_minus_the_cuts()
    {
        using var source = Synth.Tone(3).Then(Synth.Silence(4)).Then(Synth.Tone(3)).WriteTemp();
        var cuts = new[] { Range(3.15, 6.85), Range(8, 8.4) };
        using var output = new TempFile(".wav");

        long frames = EditExporter.ExportTrack(new EditTrack(source.Path, Target(output.Path)), cuts, Crossfade);

        long expected = 10 * AudioFormat.SampleRate - (long)Math.Round((3.7 + 0.4) * AudioFormat.SampleRate);
        Assert.Equal(expected, frames);
        Assert.Equal(expected, Read(output.Path).Length / AudioFormat.Channels);
    }

    [Fact]
    public void Cuts_at_the_start_and_the_end_are_removed_too()
    {
        using var source = Synth.Silence(1).Then(Synth.Tone(2)).Then(Synth.Silence(1)).WriteTemp();
        using var output = new TempFile(".wav");

        long frames = EditExporter.ExportTrack(new EditTrack(source.Path, Target(output.Path)), [Range(0, 0.9), Range(3.1, 4)], Crossfade);

        Assert.Equal(2.2 * AudioFormat.SampleRate, frames, 1);
    }

    [Fact]
    public async Task Tracks_get_the_same_cuts_and_stay_in_sync()
    {
        // The track counts its own frames, so any drift between the files would show in its samples.
        using var mix = Synth.Tone(2).Then(Synth.Silence(3)).Then(Synth.Tone(2)).WriteTemp();
        using var track = Synth.Counter(7).WriteTemp();
        using var mixOut = new TempFile(".wav");
        using var trackOut = new TempFile(".wav");
        CutRegion[] regions = [new(TimeSpan.FromSeconds(2.15), TimeSpan.FromSeconds(4.85), CutKind.Silence)];

        var written = await EditExporter.ExportAsync(
            [new EditTrack(mix.Path, Target(mixOut.Path)), new EditTrack(track.Path, Target(trackOut.Path))],
            regions, Crossfade);

        Assert.Equal([mixOut.Path, trackOut.Path], written);
        var editedMix = Read(mixOut.Path);
        var editedTrack = Read(trackOut.Path);
        Assert.Equal(editedMix.Length, editedTrack.Length);

        // Away from the crossfade, frame n of the edit is frame n (before the cut) or n + cut (after it) of the source.
        long cut = (long)Math.Round(2.7 * AudioFormat.SampleRate);
        long cutAt = (long)Math.Round(2.15 * AudioFormat.SampleRate);
        foreach (long frame in new long[] { 1000, cutAt - 2000, cutAt + 2000, editedTrack.Length / 2 - 10 })
        {
            long expected = frame < cutAt ? frame : frame + cut;
            Assert.Equal(Synth.CounterValue(expected), editedTrack[frame * 2], 4);
        }
    }

    [Fact]
    public void Cut_points_are_crossfaded_without_a_click()
    {
        // Two tones meeting out of phase jump by up to twice their level without a crossfade.
        using var source = Synth.Tone(1, frequency: 440).Then(Synth.Tone(1, frequency: 300, phase: Math.PI)).WriteTemp();
        using var output = new TempFile(".wav");

        EditExporter.ExportTrack(new EditTrack(source.Path, Target(output.Path)), [Range(0.5003, 1.4)], Crossfade);

        var samples = Read(output.Path);
        float largestStep = 0;
        for (int i = 2; i < samples.Length; i += 2) largestStep = Math.Max(largestStep, Math.Abs(samples[i] - samples[i - 2]));
        // A 440 Hz tone at 0.5 moves at most 2π·440/48000·0.5 ≈ 0.029 per sample.
        Assert.True(largestStep < 0.05f, $"step {largestStep}");
    }

    [Fact]
    public void The_original_is_never_changed_and_an_edit_never_overwrites_another()
    {
        using var source = Synth.Tone(1).Then(Synth.Silence(2)).WriteTemp();
        var before = SHA256.HashData(File.ReadAllBytes(source.Path));
        var first = EditExporter.EditedPath(source.Path, ".wav");
        Assert.EndsWith(EditExporter.Suffix + ".wav", first);

        try
        {
            EditExporter.ExportTrack(new EditTrack(source.Path, Target(first)), [Range(1.15, 2)], Crossfade);
            var second = EditExporter.EditedPath(source.Path, ".wav");

            Assert.NotEqual(first, second);
            Assert.EndsWith(EditExporter.Suffix + " (2).wav", second);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source.Path)));
            Assert.Throws<IOException>(() => EditExporter.ExportTrack(new EditTrack(source.Path, Target(source.Path)), [], Crossfade));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source.Path)));
        }
        finally
        {
            File.Delete(first);
        }
    }

    [Fact]
    public void A_flac_edit_comes_out_the_expected_length()
    {
        using var source = Synth.Tone(2).Then(Synth.Silence(2)).WriteTemp();
        using var output = new TempFile(".flac");

        long frames = EditExporter.ExportTrack(
            new EditTrack(source.Path, new RecordingTarget(output.Path, new FlacEncoder())), [Range(2.15, 3.85)], Crossfade);

        Assert.Equal((long)Math.Round(2.3 * AudioFormat.SampleRate), frames);
        Assert.True(new FileInfo(output.Path).Length > 0);
    }

    [Fact]
    public void The_preview_with_the_cut_is_shorter_by_the_cut()
    {
        using var source = Synth.Tone(3).Then(Synth.Silence(3)).Then(Synth.Tone(3)).WriteTemp();
        var cut = Range(3.15, 5.85);

        var original = EditPreview.Render(source.Path, cut, withCut: false);
        var edited = EditPreview.Render(source.Path, cut, withCut: true);

        Assert.Equal((2 + 2.7 + 2) * AudioFormat.SampleRate * AudioFormat.Channels, original.Length, 2);
        Assert.Equal(original.Length - 2.7 * AudioFormat.SampleRate * AudioFormat.Channels, edited.Length, 2);
    }

    [Fact]
    public async Task Previews_are_heard_on_the_monitor_but_never_recorded()
    {
        using var engine = new AudioEngine();
        var recording = new CapturingEncoder();
        engine.Recorder.Start(new RecordingTarget("mix.raw", recording));
        engine.Preview.Play(Enumerable.Repeat(0.25f, 960).ToArray());

        var monitor = new float[960];
        engine.Bus.Read(monitor, 0, monitor.Length);
        await engine.Recorder.StopAsync();

        Assert.All(monitor, s => Assert.Equal(0.25f, s, 5));
        Assert.All(recording.Samples, s => Assert.Equal(0f, s));
        Assert.False(engine.Preview.IsPlaying);
    }

    private static TimeRange Range(double start, double end) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    private static RecordingTarget Target(string path) => new(path, new WavEncoder(floatingPoint: true));

    private static float[] Read(string path)
    {
        using var reader = new WaveFileReader(path);
        var samples = new float[reader.SampleCount * reader.WaveFormat.Channels];
        int read = reader.ToSampleProvider().Read(samples, 0, samples.Length);
        return samples[..read];
    }
}

/// <summary>Builds test audio in the mix format (48 kHz stereo float).</summary>
internal sealed class Synth(float[] samples)
{
    public float[] Samples { get; } = samples;

    public static Synth Tone(double seconds, double frequency = 440, double level = 0.5, double phase = 0) =>
        Generate(seconds, f => (float)(level * Math.Sin(2 * Math.PI * frequency * f / AudioFormat.SampleRate + phase)));

    public static Synth Silence(double seconds) => Generate(seconds, _ => 0f);

    /// <summary>White noise at about <paramref name="levelDb"/> dBFS RMS.</summary>
    public static Synth Noise(double seconds, double levelDb)
    {
        var random = new Random(1);
        double amplitude = Math.Pow(10, levelDb / 20) * Math.Sqrt(3); // uniform noise: RMS = amplitude / √3
        return Generate(seconds, _ => (float)(amplitude * (random.NextDouble() * 2 - 1)));
    }

    /// <summary>Each frame holds a value derived from its own index, so a shifted frame is easy to spot.</summary>
    public static Synth Counter(double seconds) => Generate(seconds, CounterValue);

    public static float CounterValue(long frame) => frame % 1000 / 1000f;

    public Synth Then(Synth next) => new([.. Samples, .. next.Samples]);

    public ISampleProvider Provider() => new ArrayProvider(Samples);

    public TempFile WriteTemp()
    {
        var file = new TempFile(".wav");
        using var writer = new WaveFileWriter(file.Path, AudioFormat.Mix);
        writer.WriteSamples(Samples, 0, Samples.Length);
        return file;
    }

    private static Synth Generate(double seconds, Func<long, float> frame)
    {
        long frames = (long)Math.Round(seconds * AudioFormat.SampleRate);
        var samples = new float[frames * AudioFormat.Channels];
        for (long f = 0; f < frames; f++)
        {
            samples[f * 2] = samples[f * 2 + 1] = frame(f);
        }

        return new Synth(samples);
    }

    private sealed class ArrayProvider(float[] samples) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat => AudioFormat.Mix;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}

internal sealed class TempFile(string extension) : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"podcastrecorder-{Guid.NewGuid():N}{extension}");

    public void Dispose() => File.Delete(Path);
}
