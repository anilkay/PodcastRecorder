using NAudio.Wave;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Tests;

public class CrossfadeTests
{
    private static WaveStream OpenWav(string path) => new WaveFileReader(path);

    [Fact]
    public void Next_file_overlaps_the_previous_one_during_the_crossfade()
    {
        using var first = TempWav.Create(TimeSpan.FromSeconds(2), 0.5f);
        using var second = TempWav.Create(TimeSpan.FromSeconds(2), 0.25f);
        var deck = new PlaybackDeck(OpenWav) { CrossfadeDuration = TimeSpan.FromMilliseconds(100) };
        var buffer = new float[9600];

        deck.Play(first.Path);
        deck.Read(buffer, 0, buffer.Length);
        deck.Play(second.Path);

        deck.Read(buffer, 0, buffer.Length);
        Assert.Equal(0.375f, buffer[4800], 1);
        Assert.Equal(0.25f, buffer[^1], 2);

        deck.Read(buffer, 0, buffer.Length);
        Assert.All(buffer.Skip(100), s => Assert.Equal(0.25f, s, 2));
        Assert.Equal(second.Path, deck.CurrentFile);
    }

    [Fact]
    public void Without_crossfade_the_switch_is_instant()
    {
        using var first = TempWav.Create(TimeSpan.FromSeconds(1), 0.5f);
        using var second = TempWav.Create(TimeSpan.FromSeconds(1), 0.25f);
        var deck = new PlaybackDeck(OpenWav);
        var buffer = new float[9600];

        deck.Play(first.Path);
        deck.Read(buffer, 0, buffer.Length);
        deck.Play(second.Path);
        deck.Read(buffer, 0, buffer.Length);

        Assert.All(buffer.Skip(100), s => Assert.Equal(0.25f, s, 2));
    }

    [Fact]
    public void Track_ending_is_raised_once_when_the_crossfade_window_starts()
    {
        using var wav = TempWav.Create(TimeSpan.FromMilliseconds(500));
        var deck = new PlaybackDeck(OpenWav) { CrossfadeDuration = TimeSpan.FromMilliseconds(200) };
        var raised = 0;
        deck.TrackEnding += (_, _) => raised++;
        deck.Play(wav.Path);
        var buffer = new float[4800];

        deck.Read(buffer, 0, buffer.Length);
        Assert.Equal(0, raised);

        for (int i = 0; i < 6; i++) deck.Read(buffer, 0, buffer.Length);
        Assert.Equal(1, raised);
    }
}
