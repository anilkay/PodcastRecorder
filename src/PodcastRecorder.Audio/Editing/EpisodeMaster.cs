using NAudio.Wave;
using PodcastRecorder.Audio.Denoising;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Editing;

/// <summary>What the automatic edit does to a recording besides the cuts.</summary>
public sealed record MasterOptions
{
    /// <summary>Clean the recording with RNNoise.</summary>
    public bool ReduceNoise { get; init; }

    /// <summary>Bring the speech to <see cref="TargetLufs"/>.</summary>
    public bool Normalize { get; init; } = true;

    public double TargetLufs { get; init; } = Normalizer.PodcastTargetLufs;

    /// <summary>Music played before the recording; its end runs under the first words.</summary>
    public string? IntroPath { get; init; }

    /// <summary>Music played after the recording; it starts under the last words.</summary>
    public string? OutroPath { get; init; }

    /// <summary>How long the music and the speech overlap, at most (and at most half of the music).</summary>
    public TimeSpan Overlap { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>The music's level under speech, relative to its level alone.</summary>
    public double DuckDb { get; init; } = -14;

    /// <summary>How loud the music alone is, relative to the speech.</summary>
    public double MusicDb { get; init; } = -3;
}

/// <summary>What <see cref="EpisodeMaster"/> wrote.</summary>
/// <param name="MeasuredLufs">Loudness of the speech after the cuts, before the gain.</param>
/// <param name="SpeechStart">Where the recording starts in the file (after the intro).</param>
public sealed record MasterResult(string Path, TimeSpan Length, TimeSpan Removed, double MeasuredLufs, double GainDb, TimeSpan SpeechStart);

/// <summary>
/// Turns a recording into a file ready to publish, in one go: the cuts are taken out, the noise
/// is reduced, the speech is brought to the podcast loudness with the limiter catching peaks, and
/// intro and outro music are put around it, ducked where they run under the speech. The original
/// is left untouched; its tags and chapters are carried over.
/// </summary>
public static class EpisodeMaster
{
    /// <summary>Added to the name of the finished file: podcast-… .flac → podcast-…_yayin.mp3.</summary>
    public const string Suffix = "_yayin";

    private const int BlockFrames = 8192;

    /// <summary>The music moves between its full and its ducked level over this long.</summary>
    private static readonly TimeSpan DuckFade = TimeSpan.FromSeconds(0.6);

    public static Task<MasterResult> ExportAsync(
        string sourcePath, RecordingTarget output, IEnumerable<CutRegion> regions, MasterOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var list = regions.ToList();
        return Task.Run(() => Export(sourcePath, output, list, options, progress, cancellationToken), cancellationToken);
    }

    private static MasterResult Export(
        string sourcePath, RecordingTarget output, List<CutRegion> regions, MasterOptions options,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(output.Path), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(Texts.Get("Edit_WouldOverwrite"));
        }

        // The cut speech goes to a temporary file first: its loudness is only known at its end.
        var speechPath = Path.Combine(Path.GetTempPath(), $"podcastrecorder-kurgu-{Guid.NewGuid():N}.wav");
        try
        {
            IReadOnlyList<TimeRange> cuts;
            TimeSpan sourceLength;
            long speechFrames;
            var meter = new LoudnessMeter();
            using (var reader = AudioFiles.Open(sourcePath))
            using (var writer = new WaveFileWriter(speechPath, AudioFormat.Mix))
            {
                sourceLength = reader.TotalTime;
                cuts = CutList.Merge(regions, sourceLength);
                var source = FormatConversion.ToMixFormat(reader.ToSampleProvider());
                using var denoised = options.ReduceNoise ? new DenoisedFile(source) : null;
                source = (ISampleProvider?)denoised ?? source;
                long total = CutList.ToFrame(sourceLength, AudioFormat.SampleRate);
                var part = progress is null ? null : new Scaled(progress, 0, 0.55);
                speechFrames = CutRenderer.Render(source, cuts, EditExporter.DefaultCrossfade, total, (samples, offset, count) =>
                {
                    meter.Process(samples.AsSpan(offset, count), integrate: true);
                    writer.WriteSamples(samples, offset, count);
                }, part, cancellationToken);
            }

            double measured = meter.IntegratedLufs;
            double gainDb = options.Normalize ? Normalizer.GainFor(measured, options.TargetLufs) : 0;
            // Music sits a little under the speech, wherever the speech ends up.
            double speechLufs = double.IsFinite(measured) ? measured + gainDb : options.TargetLufs;
            var intro = Music.Load(options.IntroPath, speechLufs + options.MusicDb, cancellationToken);
            var outro = Music.Load(options.OutroPath, speechLufs + options.MusicDb, cancellationToken);

            var plan = Timeline.Plan(speechFrames, intro?.Frames ?? 0, outro?.Frames ?? 0, options.Overlap, DuckFade);
            try
            {
                using var speech = new WaveFileReader(speechPath);
                using var encoder = output.Encoder;
                encoder.Open(output.Path, AudioFormat.Mix);
                Mix(speech.ToSampleProvider(), intro, outro, plan, gainDb, options.DuckDb, encoder,
                    progress is null ? null : new Scaled(progress, 0.55, 0.45), cancellationToken);
            }
            catch
            {
                EditExporter.TryDelete(output.Path);
                throw;
            }

            var speechStart = AudioFormat.FramesToTime(plan.SpeechStart);
            EditExporter.CopyTags(sourcePath, output.Path);
            EditExporter.CopyChapters(sourcePath, output.Path, cuts, speechStart);
            var removed = cuts.Aggregate(TimeSpan.Zero, (sum, c) => sum + c.Duration);
            return new MasterResult(output.Path, AudioFormat.FramesToTime(plan.Total), removed, measured, gainDb, speechStart);
        }
        finally
        {
            EditExporter.TryDelete(speechPath);
        }
    }

    private static void Mix(
        ISampleProvider speech, Music? intro, Music? outro, Timeline plan, double gainDb, double duckDb,
        IAudioEncoder encoder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        const int channels = AudioFormat.Channels;
        float gain = ToGain(gainDb);
        float duck = ToGain(duckDb);
        var limiter = new Limiter();
        var block = new float[BlockFrames * channels];
        var voice = new float[BlockFrames * channels];

        for (long position = 0; position < plan.Total; position += BlockFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = (int)Math.Min(BlockFrames, plan.Total - position);
            var mixed = block.AsSpan(0, frames * channels);
            mixed.Clear();

            // The speech occupies [SpeechStart, SpeechStart + SpeechFrames).
            long from = Math.Max(position, plan.SpeechStart);
            long to = Math.Min(position + frames, plan.SpeechStart + plan.SpeechFrames);
            if (to > from)
            {
                int want = (int)(to - from) * channels;
                int got = ReadFully(speech, voice, want);
                int at = (int)(from - position) * channels;
                for (int i = 0; i < got; i++) mixed[at + i] = voice[i] * gain;
            }

            for (int f = 0; f < frames; f++)
            {
                long t = position + f;
                if (intro is not null && t < intro.Frames)
                {
                    float level = plan.IntroLevel(t, duck);
                    mixed[f * channels] += intro.Samples[t * channels] * level;
                    mixed[f * channels + 1] += intro.Samples[t * channels + 1] * level;
                }

                long o = t - plan.OutroStart;
                if (outro is not null && o >= 0 && o < outro.Frames)
                {
                    float level = plan.OutroLevel(t, duck);
                    mixed[f * channels] += outro.Samples[o * channels] * level;
                    mixed[f * channels + 1] += outro.Samples[o * channels + 1] * level;
                }
            }

            limiter.Process(mixed, channels);
            encoder.Write(block, 0, frames * channels);
            progress?.Report((double)(position + frames) / plan.Total);
        }
    }

    private static int ReadFully(ISampleProvider source, float[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = source.Read(buffer, total, count - total);
            if (read <= 0) break;
            total += read;
        }

        return total;
    }

    private static float ToGain(double db) => (float)Math.Pow(10, db / 20);

    /// <summary>
    /// Where everything sits, in frames. The intro plays alone, drops to the ducked level just
    /// before the speech starts and fades out under it; the outro fades in under the last words,
    /// rises once they end and plays to its own end.
    /// </summary>
    internal sealed record Timeline(long SpeechStart, long SpeechFrames, long IntroFrames, long OutroStart, long OutroFrames, long Total, long FadeFrames)
    {
        public long SpeechEnd => SpeechStart + SpeechFrames;

        public static Timeline Plan(long speechFrames, long introFrames, long outroFrames, TimeSpan overlap, TimeSpan duckFade)
        {
            long most = Math.Max(0, CutList.ToFrame(overlap, AudioFormat.SampleRate));
            long introOverlap = Math.Min(Math.Min(most, introFrames / 2), speechFrames / 2);
            long outroOverlap = Math.Min(Math.Min(most, outroFrames / 2), speechFrames / 2);
            long speechStart = introFrames - introOverlap;
            long outroStart = speechStart + speechFrames - outroOverlap;
            long total = Math.Max(speechStart + speechFrames, outroFrames > 0 ? outroStart + outroFrames : 0);
            long fade = Math.Max(1, CutList.ToFrame(duckFade, AudioFormat.SampleRate));
            return new Timeline(speechStart, speechFrames, introFrames, outroStart, outroFrames, total, fade);
        }

        /// <summary>The intro's gain at frame <paramref name="t"/> of the file.</summary>
        public float IntroLevel(long t, float duck)
        {
            if (t >= IntroFrames) return 0f;
            if (t >= SpeechStart)
            {
                // Under the speech: from the ducked level down to nothing.
                long length = IntroFrames - SpeechStart;
                return duck * (1f - (float)(t - SpeechStart) / length);
            }

            long fadeStart = Math.Max(0, SpeechStart - FadeFrames);
            if (t < fadeStart) return 1f;
            return Lerp(1f, duck, (float)(t - fadeStart) / (SpeechStart - fadeStart));
        }

        /// <summary>The outro's gain at frame <paramref name="t"/> of the file.</summary>
        public float OutroLevel(long t, float duck)
        {
            if (t < OutroStart) return 0f;
            if (t < SpeechEnd)
            {
                // Under the speech: up from nothing to the ducked level.
                long length = SpeechEnd - OutroStart;
                return duck * ((float)(t - OutroStart + 1) / length);
            }

            long rise = Math.Min(FadeFrames, Math.Max(1, OutroStart + OutroFrames - SpeechEnd));
            float start = SpeechEnd > OutroStart ? duck : 1f;
            return t - SpeechEnd >= rise ? 1f : Lerp(start, 1f, (float)(t - SpeechEnd) / rise);
        }

        private static float Lerp(float a, float b, float x) => a + (b - a) * Math.Clamp(x, 0f, 1f);
    }

    /// <summary>A piece of music held in memory in the mix format, already at the level it plays at alone.</summary>
    internal sealed class Music(float[] samples)
    {
        /// <summary>Longer files are cut off here; an intro is not meant to be a whole song.</summary>
        public static TimeSpan MaximumLength { get; } = TimeSpan.FromMinutes(10);

        public float[] Samples { get; } = samples;

        public long Frames { get; } = samples.Length / AudioFormat.Channels;

        public static Music? Load(string? path, double targetLufs, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            using var reader = AudioFiles.Open(path);
            var source = FormatConversion.ToMixFormat(reader.ToSampleProvider());
            long limit = CutList.ToFrame(MaximumLength, AudioFormat.SampleRate) * AudioFormat.Channels;
            var samples = new List<float>();
            var buffer = new float[BlockFrames * AudioFormat.Channels];
            var meter = new LoudnessMeter();
            int read;
            while (samples.Count < limit && (read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                read -= read % AudioFormat.Channels;
                meter.Process(buffer.AsSpan(0, read), integrate: true);
                samples.AddRange(buffer.AsSpan(0, read));
            }

            if (samples.Count == 0) return null;

            var all = samples.ToArray();
            float gain = ToGain(Normalizer.GainFor(meter.IntegratedLufs, targetLufs));
            for (int i = 0; i < all.Length; i++) all[i] *= gain;
            return new Music(all);
        }
    }

    /// <summary>Maps 0..1 onto a part of the whole job's progress.</summary>
    private sealed class Scaled(IProgress<double> target, double start, double share) : IProgress<double>
    {
        public void Report(double value) => target.Report(start + Math.Clamp(value, 0, 1) * share);
    }
}
