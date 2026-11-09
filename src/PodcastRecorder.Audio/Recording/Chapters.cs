using PodcastRecorder.Audio.Editing;

namespace PodcastRecorder.Audio.Recording;

/// <summary>A chapter of a recording: where it starts and its name. It lasts until the next one starts.</summary>
public sealed record Chapter(TimeSpan Start, string Title);

/// <summary>
/// Chapters in audio files, which podcast players show as a list to jump between: ID3v2 CHAP/CTOC
/// frames in MP3, a QuickTime chapter track plus a Nero "chpl" list in M4A (Apple Podcasts reads the
/// first, most other players the second), and CHAPTERxxx comments in FLAC. Other formats have none.
/// </summary>
public static class Chapters
{
    /// <summary>Markers closer than this to the previous chapter are dropped (a double key press).</summary>
    public static TimeSpan MinimumGap { get; } = TimeSpan.FromSeconds(1);

    /// <summary>Whether <see cref="Write"/> can store chapters in a file with this extension.</summary>
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".m4a" or ".flac";

    /// <summary>
    /// Turns the times a marker was dropped at into chapters. The first chapter always starts at
    /// zero, so the part before the first marker is a chapter too; no markers means no chapters.
    /// </summary>
    /// <param name="title">The name of the chapter with the given 1-based number.</param>
    public static IReadOnlyList<Chapter> FromMarkers(IEnumerable<TimeSpan> markers, Func<int, string> title)
    {
        var starts = new List<TimeSpan>();
        foreach (var marker in markers.Order())
        {
            if (starts.Count == 0)
            {
                starts.Add(TimeSpan.Zero);
            }

            if (marker - starts[^1] >= MinimumGap) starts.Add(marker);
        }

        return starts.Select((start, i) => new Chapter(start, title(i + 1))).ToList();
    }

    /// <summary>
    /// Moves chapters to where they land once <paramref name="cuts"/> (merged, in order) are taken
    /// out. A chapter that starts inside a cut starts where the cut was; chapters that end up on
    /// top of one another keep only the last.
    /// </summary>
    public static IReadOnlyList<Chapter> AfterCuts(IReadOnlyList<Chapter> chapters, IReadOnlyList<TimeRange> cuts)
    {
        var moved = new List<Chapter>();
        foreach (var chapter in chapters.OrderBy(c => c.Start))
        {
            var removed = TimeSpan.Zero;
            var start = chapter.Start;
            foreach (var cut in cuts)
            {
                if (cut.Start >= chapter.Start) break;
                removed += (chapter.Start < cut.End ? chapter.Start : cut.End) - cut.Start;
            }

            start -= removed;
            if (moved.Count > 0 && start - moved[^1].Start < MinimumGap) moved.RemoveAt(moved.Count - 1);
            moved.Add(chapter with { Start = start < TimeSpan.Zero ? TimeSpan.Zero : start });
        }

        if (moved.Count > 0 && moved[0].Start != TimeSpan.Zero) moved[0] = moved[0] with { Start = TimeSpan.Zero };
        return moved;
    }

    /// <summary>
    /// Writes <paramref name="chapters"/> into the file, replacing any it had; an empty list removes
    /// them. Does nothing for formats without chapters. Throws an <see cref="IOException"/> with a
    /// message in the UI language on failure.
    /// </summary>
    public static void Write(string path, IReadOnlyList<Chapter> chapters)
    {
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3":
                    Id3Chapters.Write(path, chapters);
                    break;
                case ".m4a":
                    Mp4Chapters.Write(path, chapters);
                    break;
                case ".flac":
                    VorbisChapters.Write(path, chapters);
                    break;
            }
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                                       or InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new IOException(Texts.Format("Chapters_WriteFailed", Path.GetFileName(path), ex.Message), ex);
        }
    }

    /// <summary>The chapters stored in the file, in order; empty if it has none or cannot be read.</summary>
    public static IReadOnlyList<Chapter> Read(string path)
    {
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp3" => Id3Chapters.Read(path),
                ".m4a" => Mp4Chapters.Read(path),
                ".flac" => VorbisChapters.Read(path),
                _ => [],
            };
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                                       or InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>The length of the audio in the file, for the end of the last chapter.</summary>
    internal static TimeSpan Duration(TagLib.File file) => file.Properties?.Duration ?? TimeSpan.Zero;
}

/// <summary>ID3v2 chapter frames (CHAP), listed in order by a top-level table of contents (CTOC).</summary>
internal static class Id3Chapters
{
    private const string TocId = "toc";

    public static void Write(string path, IReadOnlyList<Chapter> chapters)
    {
        using var file = TagLib.File.Create(path);
        var tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
        foreach (var frame in tag.GetFrames().Where(f => f is TagLib.Id3v2.ChapterFrame or TagLib.Id3v2.TableOfContentsFrame).ToList())
        {
            tag.RemoveFrame(frame);
        }

        if (chapters.Count > 0)
        {
            var total = Chapters.Duration(file);
            var toc = new TagLib.Id3v2.TableOfContentsFrame(TocId) { IsTopLevel = true, IsOrdered = true };
            for (int i = 0; i < chapters.Count; i++)
            {
                var end = i + 1 < chapters.Count ? chapters[i + 1].Start : total;
                var id = $"ch{i + 1}";
                tag.AddFrame(new TagLib.Id3v2.ChapterFrame(id, chapters[i].Title)
                {
                    StartMilliseconds = Milliseconds(chapters[i].Start),
                    EndMilliseconds = Math.Max(Milliseconds(end), Milliseconds(chapters[i].Start)),
                    // "No byte offsets": players go by the times.
                    StartByteOffset = uint.MaxValue,
                    EndByteOffset = uint.MaxValue,
                });
                toc.ChapterIds.Add(id);
            }

            tag.AddFrame(toc);
        }

        file.Save();
    }

    public static IReadOnlyList<Chapter> Read(string path)
    {
        using var file = TagLib.File.Create(path);
        if (file.GetTag(TagLib.TagTypes.Id3v2) is not TagLib.Id3v2.Tag tag) return [];
        return tag.GetFrames<TagLib.Id3v2.ChapterFrame>()
            .Select(f => new Chapter(TimeSpan.FromMilliseconds(f.StartMilliseconds), Title(f)))
            .OrderBy(c => c.Start)
            .ToList();
    }

    private static string Title(TagLib.Id3v2.ChapterFrame frame) =>
        frame.SubFrames.OfType<TagLib.Id3v2.TextInformationFrame>().FirstOrDefault(f => f.FrameId == "TIT2")?.Text.FirstOrDefault() ?? "";

    private static uint Milliseconds(TimeSpan time) => (uint)Math.Clamp(Math.Round(time.TotalMilliseconds), 0, uint.MaxValue - 1);
}

/// <summary>The CHAPTER001=00:00:00.000 / CHAPTER001NAME=… comments FLAC players read.</summary>
internal static class VorbisChapters
{
    public static void Write(string path, IReadOnlyList<Chapter> chapters)
    {
        using var file = TagLib.File.Create(path);
        var tag = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph, create: true);
        foreach (var field in tag.Where(f => f.StartsWith("CHAPTER", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            tag.RemoveField(field);
        }

        for (int i = 0; i < chapters.Count; i++)
        {
            var key = $"CHAPTER{i + 1:000}";
            tag.SetField(key, chapters[i].Start.ToString(@"hh\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture));
            tag.SetField(key + "NAME", chapters[i].Title);
        }

        file.Save();
    }

    public static IReadOnlyList<Chapter> Read(string path)
    {
        using var file = TagLib.File.Create(path);
        if (file.GetTag(TagLib.TagTypes.Xiph) is not TagLib.Ogg.XiphComment tag) return [];
        var chapters = new List<Chapter>();
        for (int i = 1; i < 1000; i++)
        {
            var key = $"CHAPTER{i:000}";
            var time = tag.GetFirstField(key);
            if (time is null) break;
            if (TimeSpan.TryParse(time, System.Globalization.CultureInfo.InvariantCulture, out var start))
            {
                chapters.Add(new Chapter(start, tag.GetFirstField(key + "NAME") ?? ""));
            }
        }

        return chapters;
    }
}
