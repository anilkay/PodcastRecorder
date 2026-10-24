using NAudio.Wave;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.Audio.Tests;

public class SourceTests
{
    private static WaveStream OpenWav(string path) => new WaveFileReader(path);

    [Fact]
    public void Mono_44k_is_converted_to_stereo_48k()
    {
        var mono = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);

        var converted = FormatConversion.ToMixFormat(new ConstantSource(0.5f, mono));

        Assert.True(AudioFormat.IsMixFormat(converted.WaveFormat));
    }

    [Fact]
    public void Deck_plays_a_file_to_the_end_and_reports_it()
    {
        using var wav = TempWav.Create(TimeSpan.FromMilliseconds(100));
        var deck = new PlaybackDeck(OpenWav);
        string? ended = null;
        deck.PlaybackEnded += (_, path) => ended = path;
        deck.Play(wav.Path);

        var buffer = new float[9600];
        int read = deck.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, read);
        Assert.Equal(0.5f, buffer[100], 2);

        deck.Read(buffer, 0, buffer.Length);

        Assert.Equal(wav.Path, ended);
        Assert.False(deck.IsPlaying);
        Assert.Equal(buffer.Length, deck.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Jingles_sound_together_and_stop_independently()
    {
        using var first = TempWav.Create(TimeSpan.FromSeconds(1), 0.25f);
        using var second = TempWav.Create(TimeSpan.FromSeconds(1), 0.25f);
        var jingles = new JinglePlayer(OpenWav);
        jingles.Play("a", first.Path);
        jingles.Play("b", second.Path);
        var buffer = new float[960];

        jingles.Read(buffer, 0, buffer.Length);
        Assert.Equal(0.5f, buffer[500], 2);

        jingles.Stop("a");
        jingles.Read(buffer, 0, buffer.Length);
        Assert.Equal(0.25f, buffer[500], 2);
        Assert.False(jingles.IsPlaying("a"));
        Assert.True(jingles.IsPlaying("b"));

        // Release the file so the temporary WAV can be deleted (Windows keeps open files locked).
        jingles.StopAll();
    }

    [Fact]
    public void Jingle_end_is_reported_with_its_key()
    {
        using var wav = TempWav.Create(TimeSpan.FromMilliseconds(10));
        var jingles = new JinglePlayer(OpenWav);
        string? ended = null;
        jingles.JingleEnded += (_, key) => ended = key;
        jingles.Play("pad-3", wav.Path);

        jingles.Read(new float[9600], 0, 9600);

        Assert.Equal("pad-3", ended);
        Assert.False(jingles.IsPlaying("pad-3"));
    }
}
