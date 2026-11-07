using System.Globalization;
using NAudio.Wave;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Editing;

public sealed class SilenceOptions
{
    /// <summary>Windows quieter than this RMS level, in dBFS, count as silence.</summary>
    public double ThresholdDb { get; init; } = -40;

    /// <summary>Only pauses at least this long are shortened.</summary>
    public TimeSpan MinimumSilence { get; init; } = TimeSpan.FromSeconds(0.8);

    /// <summary>How much of each pause stays, half on each side, so speech keeps its rhythm.</summary>
    public TimeSpan KeptGap { get; init; } = TimeSpan.FromSeconds(0.3);

    /// <summary>The level is measured over windows this long.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMilliseconds(20);
}

/// <summary>Finds long pauses in a recording by its RMS level in short windows.</summary>
public static class SilenceDetector
{
    /// <summary>Reads the whole file on the calling thread; call it from a background task.</summary>
    public static IReadOnlyList<CutRegion> DetectFile(
        string path, SilenceOptions options, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var reader = AudioFiles.Open(path);
        return Detect(reader.ToSampleProvider(), options, reader.TotalTime, progress, cancellationToken);
    }

    /// <summary>
    /// The part of each pause longer than <see cref="SilenceOptions.MinimumSilence"/> that goes,
    /// leaving <see cref="SilenceOptions.KeptGap"/> of it in place.
    /// </summary>
    /// <param name="length">Only for progress; the source is read to its end.</param>
    public static IReadOnlyList<CutRegion> Detect(
        ISampleProvider source, SilenceOptions options, TimeSpan length = default,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var regions = new List<CutRegion>();
        var halfGap = options.KeptGap / 2;
        foreach (var (quiet, levelDb) in FindQuiet(source, options.ThresholdDb, options.Window, length, progress, cancellationToken))
        {
            if (quiet.Duration < options.MinimumSilence || quiet.Duration <= options.KeptGap) continue;

            var label = string.Create(CultureInfo.InvariantCulture, $"{levelDb:0} dB");
            regions.Add(new CutRegion(quiet.Start + halfGap, quiet.End - halfGap, CutKind.Silence, label));
        }

        return regions;
    }

    /// <summary>Every run of windows below <paramref name="thresholdDb"/>, with its average level.</summary>
    internal static List<(TimeRange Range, double LevelDb)> FindQuiet(
        ISampleProvider source, double thresholdDb, TimeSpan window, TimeSpan length,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        int rate = source.WaveFormat.SampleRate;
        int channels = source.WaveFormat.Channels;
        int windowFrames = Math.Max(1, (int)Math.Round(window.TotalSeconds * rate));
        double threshold = Math.Pow(10, thresholdDb / 10); // mean square, so RMS in dB = 10·log10
        double totalFrames = Math.Max(1, length.TotalSeconds * rate);

        var runs = new List<(TimeRange, double)>();
        var buffer = new float[windowFrames * channels * 50];
        long frame = 0;
        long runStart = -1;
        double runEnergy = 0;
        long runFrames = 0;
        double sum = 0;
        int inWindow = 0;
        int read;
        int blocks = 0;

        void EndWindow(long end)
        {
            double meanSquare = sum / (inWindow * channels);
            if (meanSquare < threshold)
            {
                if (runStart < 0) runStart = end - inWindow;
                runEnergy += sum;
                runFrames += inWindow;
            }
            else
            {
                EndRun(end - inWindow);
            }

            sum = 0;
            inWindow = 0;
        }

        void EndRun(long end)
        {
            if (runStart < 0) return;
            double level = runEnergy > 0 ? 10 * Math.Log10(runEnergy / (runFrames * channels)) : -120;
            runs.Add((new TimeRange(Time(runStart, rate), Time(end, rate)), Math.Max(level, -120)));
            runStart = -1;
            runEnergy = 0;
            runFrames = 0;
        }

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = read / channels;
            for (int f = 0; f < frames; f++)
            {
                for (int c = 0; c < channels; c++)
                {
                    float s = buffer[f * channels + c];
                    sum += s * s;
                }

                frame++;
                if (++inWindow == windowFrames) EndWindow(frame);
            }

            if (++blocks % 50 == 0) progress?.Report(Math.Min(frame / totalFrames, 1));
        }

        if (inWindow > 0) EndWindow(frame);
        EndRun(frame);
        progress?.Report(1);
        return runs;
    }

    private static TimeSpan Time(long frame, int rate) => TimeSpan.FromSeconds((double)frame / rate);
}
