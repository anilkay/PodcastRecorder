using NAudio.Wave;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Editing;

/// <summary>A file to edit and where its edited copy goes.</summary>
public sealed record EditTrack(string SourcePath, RecordingTarget Output);

/// <summary>
/// Writes edited copies of a recording, leaving the original untouched. A multitrack recording's
/// tracks get the very same cuts as the mix, so they stay in sync with it and with each other.
/// </summary>
public static class EditExporter
{
    /// <summary>Added to the name of the edited copy: podcast-… .flac → podcast-…_duzenlenmis.flac.</summary>
    public const string Suffix = "_duzenlenmis";

    public static TimeSpan DefaultCrossfade { get; } = TimeSpan.FromMilliseconds(15);

    /// <summary>A free name next to <paramref name="sourcePath"/>; numbered if an edit already exists.</summary>
    public static string EditedPath(string sourcePath, string extension)
    {
        var folder = Path.GetDirectoryName(sourcePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(sourcePath) + Suffix;
        var path = Path.Combine(folder, name + extension);
        for (int n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{name} ({n}){extension}");
        }

        return path;
    }

    /// <summary>
    /// Writes every track with <paramref name="regions"/> left out. The regions are merged against
    /// the first track (the mix). A failed or cancelled export deletes what it had written.
    /// </summary>
    /// <returns>The paths written, in the order of <paramref name="tracks"/>.</returns>
    public static Task<IReadOnlyList<string>> ExportAsync(
        IReadOnlyList<EditTrack> tracks, IEnumerable<CutRegion> regions, TimeSpan? crossfade = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var list = regions.ToList();
        return Task.Run(() => Export(tracks, list, crossfade ?? DefaultCrossfade, progress, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<string> Export(
        IReadOnlyList<EditTrack> tracks, List<CutRegion> regions, TimeSpan crossfade,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (tracks.Count == 0) return [];

        TimeSpan length;
        using (var mix = AudioFiles.Open(tracks[0].SourcePath)) length = mix.TotalTime;
        var cuts = CutList.Merge(regions, length);

        var written = new List<string>();
        try
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                int index = i;
                var part = progress is null ? null : new SyncProgress(p => progress.Report((index + p) / tracks.Count));
                ExportTrack(tracks[i], cuts, crossfade, part, cancellationToken);
                written.Add(tracks[i].Output.Path);
            }
        }
        catch
        {
            foreach (var path in written) TryDelete(path);
            throw;
        }

        return written;
    }

    /// <summary>Writes one edited file, in the app's mix format, and copies the original's tags into it.</summary>
    /// <returns>The number of frames written.</returns>
    public static long ExportTrack(
        EditTrack track, IReadOnlyList<TimeRange> cuts, TimeSpan crossfade,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetFullPath(track.SourcePath), Path.GetFullPath(track.Output.Path), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(Texts.Get("Edit_WouldOverwrite"));
        }

        long frames;
        try
        {
            using var reader = AudioFiles.Open(track.SourcePath);
            using var encoder = track.Output.Encoder;
            var source = FormatConversion.ToMixFormat(reader.ToSampleProvider());
            long total = CutList.ToFrame(reader.TotalTime, AudioFormat.SampleRate);
            encoder.Open(track.Output.Path, AudioFormat.Mix);
            frames = CutRenderer.Render(source, cuts, crossfade, total, encoder.Write, progress, cancellationToken);
        }
        catch
        {
            TryDelete(track.Output.Path);
            throw;
        }

        CopyTags(track.SourcePath, track.Output.Path);
        return frames;
    }

    /// <summary>"Dosya bilgileri" (title, cover…) of the original, so the edit shows up the same in players.</summary>
    private static void CopyTags(string from, string to)
    {
        try
        {
            using var source = TagLib.File.Create(from);
            using var target = TagLib.File.Create(to);
            source.Tag.CopyTo(target.Tag, overwrite: true);
            target.Save();
        }
        catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException or IOException or NotImplementedException)
        {
            // Tags are a nicety; the edited audio is what matters.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reports on the calling thread, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
