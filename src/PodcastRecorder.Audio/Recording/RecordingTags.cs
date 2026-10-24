namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// File information (artist, title and the like) written into a finished recording. Null or
/// empty fields are left out.
/// </summary>
public sealed record RecordingTags(
    string? Title = null,
    string? Artist = null,
    string? Album = null,
    uint? Track = null,
    uint? Year = null,
    string? Genre = null,
    string? Comment = null,
    string? CoverImagePath = null);

/// <summary>
/// Writes <see cref="RecordingTags"/> with TagLib#: ID3v2.3 in MP3 (the version Windows Explorer and
/// most players read), Vorbis comments in FLAC, ASF attributes in WMA, and RIFF INFO plus ID3 in WAV.
/// </summary>
public static class TagWriter
{
    static TagWriter()
    {
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
    }

    /// <summary>Writes the tags into the file at <paramref name="path"/>; throws an <see cref="IOException"/> with a Turkish message on failure.</summary>
    public static void Write(string path, RecordingTags tags)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var wanted = TagTypesFor(path);
            if (wanted != TagLib.TagTypes.None)
            {
                foreach (TagLib.TagTypes type in Enum.GetValues<TagLib.TagTypes>())
                {
                    if (type is not (TagLib.TagTypes.None or TagLib.TagTypes.AllTags or TagLib.TagTypes.FlacMetadata)
                        && wanted.HasFlag(type))
                    {
                        file.GetTag(type, create: true);
                    }
                }
            }

            if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                id3.Version = 3;
            }

            // file.Tag combines every tag in the file, so each field reaches all of them.
            var tag = file.Tag;
            if (HasText(tags.Title)) tag.Title = tags.Title!.Trim();
            if (HasText(tags.Artist))
            {
                tag.Performers = [tags.Artist!.Trim()];
                tag.AlbumArtists = [tags.Artist!.Trim()];
            }

            if (HasText(tags.Album)) tag.Album = tags.Album!.Trim();
            if (tags.Track is { } track) tag.Track = track;
            if (tags.Year is { } year) tag.Year = year;
            if (HasText(tags.Genre)) tag.Genres = [tags.Genre!.Trim()];
            if (HasText(tags.Comment)) tag.Comment = tags.Comment!.Trim();
            if (HasText(tags.CoverImagePath) && File.Exists(tags.CoverImagePath))
            {
                tag.Pictures = [new TagLib.Picture(tags.CoverImagePath) { Type = TagLib.PictureType.FrontCover }];
            }

            if (wanted != TagLib.TagTypes.None)
            {
                // Keep only the tags this format is read with. Setting fields through file.Tag also
                // fills ID3v1 in MP3 (no Turkish letters) and MovieID plus a DivX trailer in WAV.
                file.RemoveTags(TagLib.TagTypes.AllTags & ~wanted);
            }

            file.Save();
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                                       or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new IOException($"Dosya bilgileri yazılamadı ({Path.GetFileName(path)}): {ex.Message}", ex);
        }
    }

    /// <summary>The tags each format is read with; FLAC keeps its picture blocks in FlacMetadata.</summary>
    private static TagLib.TagTypes TagTypesFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => TagLib.TagTypes.Id3v2,
        ".flac" => TagLib.TagTypes.Xiph | TagLib.TagTypes.FlacMetadata,
        ".wma" => TagLib.TagTypes.Asf,
        ".wav" => TagLib.TagTypes.RiffInfo | TagLib.TagTypes.Id3v2,
        _ => TagLib.TagTypes.None,
    };

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}
