using PodcastRecorder.Audio.Editing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public sealed class ChapterTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("podcastrecorder-chapters-");

    private static readonly Chapter[] Three =
    [
        new(TimeSpan.Zero, "Giriş"),
        new(TimeSpan.FromSeconds(1.5), "Konuk: Ayşe Öztürk"),
        new(TimeSpan.FromSeconds(3.25), "Şarkı ve kapanış"),
    ];

    [Fact]
    public void Markers_become_chapters_starting_at_zero()
    {
        var chapters = Chapters.FromMarkers(
            [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.4)], n => $"Bölüm {n}");

        Assert.Equal(
            [new(TimeSpan.Zero, "Bölüm 1"), new(TimeSpan.FromSeconds(10), "Bölüm 2"), new(TimeSpan.FromSeconds(30), "Bölüm 3")],
            chapters);
    }

    [Fact]
    public void A_marker_at_the_very_start_does_not_make_an_empty_chapter()
    {
        var chapters = Chapters.FromMarkers([TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(5)], n => $"{n}");

        Assert.Equal([TimeSpan.Zero, TimeSpan.FromSeconds(5)], chapters.Select(c => c.Start));
    }

    [Fact]
    public void No_markers_means_no_chapters() => Assert.Empty(Chapters.FromMarkers([], n => $"{n}"));

    [Fact]
    public void Chapters_move_back_by_what_was_cut_before_them()
    {
        Chapter[] chapters = [new(TimeSpan.Zero, "a"), new(TimeSpan.FromSeconds(10), "b"), new(TimeSpan.FromSeconds(20), "c")];
        TimeRange[] cuts =
        [
            new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),   // before b and c
            new(TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(22)), // c starts inside it
        ];

        var moved = Chapters.AfterCuts(chapters, cuts);

        Assert.Equal([TimeSpan.Zero, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)], moved.Select(c => c.Start));
        Assert.Equal(["a", "b", "c"], moved.Select(c => c.Title));
    }

    [Fact]
    public void Chapters_squeezed_together_by_a_cut_keep_the_last()
    {
        Chapter[] chapters = [new(TimeSpan.Zero, "a"), new(TimeSpan.FromSeconds(10), "b"), new(TimeSpan.FromSeconds(12), "c")];

        var moved = Chapters.AfterCuts(chapters, [new(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(13))]);

        Assert.Equal(["a", "c"], moved.Select(c => c.Title));
        Assert.Equal(TimeSpan.FromSeconds(9), moved[1].Start);
    }

    [Fact]
    public async Task Mp3_gets_id3_chap_and_ctoc_frames()
    {
        var path = await RecordAsync(".mp3", () => new Mp3Encoder(128));

        Chapters.Write(path, Three);

        Assert.Equal(Three, Chapters.Read(path));
        using var file = TagLib.File.Create(path);
        var tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
        var toc = Assert.Single(tag.GetFrames<TagLib.Id3v2.TableOfContentsFrame>());
        Assert.True(toc.IsTopLevel);
        Assert.Equal(["ch1", "ch2", "ch3"], toc.ChapterIds);
        var last = tag.GetFrames<TagLib.Id3v2.ChapterFrame>().Single(f => f.Id == "ch3");
        Assert.InRange(last.EndMilliseconds, 4900u, 5200u);
    }

    [Fact]
    public async Task Flac_gets_chapter_comments()
    {
        var path = await RecordAsync(".flac", () => new FlacEncoder());

        Chapters.Write(path, Three);

        Assert.Equal(Three, Chapters.Read(path));
    }

    [Fact]
    public async Task M4a_gets_a_chapter_track_and_a_nero_list_and_still_plays()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = await RecordAsync(".m4a", () => new M4aEncoder(128));
        var before = AudioLength(path);

        Chapters.Write(path, Three);

        Assert.Equal(Three, Chapters.Read(path));
        Assert.Equal(before, AudioLength(path));
        AssertOneChapterTrack(path);
    }

    [Fact]
    public async Task M4a_chapters_can_be_written_again_and_survive_tags()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = await RecordAsync(".m4a", () => new M4aEncoder(128));

        Chapters.Write(path, [new(TimeSpan.Zero, "eski")]);
        TagWriter.Write(path, new RecordingTags(Title: "Bölüm 7", Artist: "Anıl"));
        Chapters.Write(path, Three);

        Assert.Equal(Three, Chapters.Read(path));
        AssertOneChapterTrack(path);
        using var file = TagLib.File.Create(path);
        Assert.Equal("Bölüm 7", file.Tag.Title);
        Assert.InRange(file.Properties.Duration.TotalSeconds, 4.8, 5.3);
    }

    [Fact]
    public async Task Recorder_keeps_markers_in_file_time()
    {
        var path = Path.Combine(_folder.FullName, "kayit.wav");
        var recorder = new Recorder();
        Assert.Null(recorder.AddMarker());

        recorder.Start(path, new WavEncoder());
        recorder.Write(new float[AudioFormat.SampleRate * AudioFormat.Channels]);
        var first = recorder.AddMarker();
        recorder.Pause();
        recorder.Write(new float[AudioFormat.SampleRate * AudioFormat.Channels]); // not recorded
        var second = recorder.AddMarker();
        await recorder.StopAsync();

        Assert.Equal(TimeSpan.FromSeconds(1), first);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], recorder.Markers);
        Assert.Equal(second, first);
    }

    /// <summary>The audio is untouched: same length as decoded by Media Foundation.</summary>
    private static TimeSpan AudioLength(string path)
    {
        using var reader = new NAudio.Wave.MediaFoundationReader(path);
        return reader.TotalTime;
    }

    private static void AssertOneChapterTrack(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int Count(string text)
        {
            var pattern = System.Text.Encoding.ASCII.GetBytes(text);
            int n = 0;
            for (int i = bytes.AsSpan().IndexOf(pattern); i >= 0;)
            {
                n++;
                int next = bytes.AsSpan(i + 1).IndexOf(pattern);
                i = next < 0 ? -1 : i + 1 + next;
            }

            return n;
        }

        Assert.Equal(1, Count("chpl"));
        Assert.Equal(1, Count("tref"));
        Assert.Equal(1, Count("gmhd"));
    }

    private async Task<string> RecordAsync(string extension, Func<IAudioEncoder> encoder)
    {
        var path = Path.Combine(_folder.FullName, "bolum" + extension);
        var recorder = new Recorder();
        recorder.Start(path, encoder());
        var second = new float[AudioFormat.SampleRate * AudioFormat.Channels];
        for (int i = 0; i < second.Length; i++) second[i] = 0.2f * MathF.Sin(i * 0.03f);
        for (int s = 0; s < 5; s++) recorder.Write(second);
        await recorder.StopAsync();
        return path;
    }

    public void Dispose() => _folder.Delete(recursive: true);
}
