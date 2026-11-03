using NAudio.Wave;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Editing;

/// <summary>Short clips around a cut, to hear it before exporting.</summary>
public static class EditPreview
{
    public static TimeSpan DefaultContext { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// <paramref name="context"/> before and after <paramref name="cut"/> in <see cref="AudioFormat.Mix"/>,
    /// with the cut left out (and crossfaded, as in the export) when <paramref name="withCut"/> is set.
    /// Reads the file on the calling thread.
    /// </summary>
    public static float[] Render(string path, TimeRange cut, bool withCut, TimeSpan? context = null, TimeSpan? crossfade = null)
    {
        var around = context ?? DefaultContext;
        using var reader = AudioFiles.Open(path);
        var start = cut.Start - around < TimeSpan.Zero ? TimeSpan.Zero : cut.Start - around;
        var end = cut.End + around > reader.TotalTime ? reader.TotalTime : cut.End + around;
        if (end <= start) return [];

        reader.CurrentTime = start;
        var source = FormatConversion.ToMixFormat(reader.ToSampleProvider());
        var clip = new float[CutList.ToFrame(end - start, AudioFormat.SampleRate) * AudioFormat.Channels];
        int filled = 0;
        int read;
        while (filled < clip.Length && (read = source.Read(clip, filled, clip.Length - filled)) > 0) filled += read;
        if (!withCut) return clip[..filled];

        var edited = new List<float>(filled);
        var local = new TimeRange(cut.Start - start, cut.End - start);
        CutRenderer.Render(new ArraySource(clip, filled), [local], crossfade ?? EditExporter.DefaultCrossfade,
            filled / AudioFormat.Channels, (buffer, offset, count) => edited.AddRange(buffer.AsSpan(offset, count)));
        return [.. edited];
    }

    private sealed class ArraySource(float[] samples, int count) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat => AudioFormat.Mix;

        public int Read(float[] buffer, int offset, int wanted)
        {
            int n = Math.Min(wanted, count - _position);
            Array.Copy(samples, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}
