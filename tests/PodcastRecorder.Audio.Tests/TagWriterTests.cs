using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public sealed class TagWriterTests : IDisposable
{
    /// <summary>A valid 1x1 PNG, enough for a cover picture.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("podcastrecorder-tags-");

    private static readonly RecordingTags Tags = new(
        Title: "Bölüm 12: Şarkılar ve sohbet",
        Artist: "Anıl Kay",
        Album: "Haftalık Podcast",
        Track: 12,
        Year: 2026,
        Genre: "Podcast",
        Comment: "Konuk: Ayşe");

    [Fact]
    public Task Wav_gets_the_tags_and_the_cover() => WritesAndReadsBack(".wav", () => new WavEncoder());

    [Fact]
    public Task Flac_gets_the_tags_and_the_cover() => WritesAndReadsBack(".flac", () => new FlacEncoder());

    [Fact]
    public Task Mp3_gets_id3v23_tags_and_the_cover() =>
        OperatingSystem.IsWindows() ? WritesAndReadsBack(".mp3", () => new Mp3Encoder(128)) : Task.CompletedTask;

    [Fact]
    public Task Wma_gets_the_tags_and_the_cover() =>
        OperatingSystem.IsWindows() ? WritesAndReadsBack(".wma", () => new WmaEncoder(128)) : Task.CompletedTask;

    [Fact]
    public async Task Empty_fields_are_left_out()
    {
        var path = await RecordAsync(".wav", () => new WavEncoder());

        TagWriter.Write(path, new RecordingTags(Title: "Sadece başlık", Artist: " ", Album: null));

        using var file = TagLib.File.Create(path);
        Assert.Equal("Sadece başlık", file.Tag.Title);
        Assert.Empty(file.Tag.Performers);
        Assert.Null(file.Tag.Album);
        Assert.Empty(file.Tag.Pictures);
    }

    [Fact]
    public void A_file_that_is_not_audio_gives_a_readable_error()
    {
        var path = Path.Combine(_folder.FullName, "bozuk.mp3");
        File.WriteAllText(path, "bu bir ses dosyası değil");

        var error = Assert.Throws<IOException>(() => TagWriter.Write(path, Tags));

        Assert.StartsWith("Dosya bilgileri yazılamadı (bozuk.mp3)", error.Message);
    }

    private async Task WritesAndReadsBack(string extension, Func<IAudioEncoder> encoder)
    {
        var path = await RecordAsync(extension, encoder);
        var cover = Path.Combine(_folder.FullName, "kapak.png");
        File.WriteAllBytes(cover, TinyPng);

        TagWriter.Write(path, Tags with { CoverImagePath = cover });

        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        Assert.Equal(Tags.Title, tag.Title);
        Assert.Equal([Tags.Artist!], tag.Performers);
        Assert.Equal(Tags.Album, tag.Album);
        Assert.Equal(12u, tag.Track);
        Assert.Equal(2026u, tag.Year);
        Assert.Equal(["Podcast"], tag.Genres);
        Assert.Equal(Tags.Comment, tag.Comment);
        var picture = Assert.Single(tag.Pictures);
        Assert.Equal(TagLib.PictureType.FrontCover, picture.Type);
        Assert.Equal(TinyPng, picture.Data.Data);
        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
        {
            Assert.Equal(3, id3.Version);
        }

        var expected = extension switch
        {
            ".mp3" => TagLib.TagTypes.Id3v2,
            ".wav" => TagLib.TagTypes.RiffInfo | TagLib.TagTypes.Id3v2,
            ".flac" => TagLib.TagTypes.Xiph | TagLib.TagTypes.FlacMetadata,
            _ => TagLib.TagTypes.Asf,
        };
        // TagTypes also lists tags TagLib creates in memory on opening; this is what was written.
        Assert.Equal(expected, file.TagTypesOnDisk);

        // The audio is still readable after the tags were added.
        Assert.True(file.Properties.Duration > TimeSpan.FromSeconds(0.4), $"duration {file.Properties.Duration}");
    }

    private async Task<string> RecordAsync(string extension, Func<IAudioEncoder> encoder)
    {
        var path = Path.Combine(_folder.FullName, "bolum" + extension);
        var recorder = new Recorder();
        recorder.Start(path, encoder());
        var samples = new float[AudioFormat.SampleRate * AudioFormat.Channels / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = 0.2f * MathF.Sin(i * 0.05f);
        recorder.Write(samples);
        await recorder.StopAsync();
        return path;
    }

    public void Dispose() => _folder.Delete(recursive: true);
}
