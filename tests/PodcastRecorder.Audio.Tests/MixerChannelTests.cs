using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.Audio.Tests;

public class MixerChannelTests
{
    [Fact]
    public void Volume_change_ramps_over_one_block_then_holds()
    {
        var channel = new MixerChannel("test", new ConstantSource(1f)) { Volume = 0.5f };
        var buffer = new float[960];

        channel.Read(buffer, 0, buffer.Length);
        Assert.True(buffer[0] > 0.5f, "first frame should still be close to the old gain");
        Assert.Equal(0.5f, buffer[^1], 3);

        channel.Read(buffer, 0, buffer.Length);
        Assert.All(buffer, s => Assert.Equal(0.5f, s, 5));
    }

    [Fact]
    public void Mute_silences_the_channel()
    {
        var channel = new MixerChannel("test", new ConstantSource(1f)) { IsMuted = true };
        var buffer = new float[960];

        channel.Read(buffer, 0, buffer.Length);
        channel.Read(buffer, 0, buffer.Length);

        Assert.All(buffer, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Meter_reports_post_fader_peak_and_resets()
    {
        var channel = new MixerChannel("test", new ConstantSource(0.8f));
        channel.Read(new float[480], 0, 480);

        var level = channel.Meter.ReadAndReset();

        Assert.Equal(0.8f, level.Left, 5);
        Assert.Equal(0.8f, level.Right, 5);
        Assert.Equal(0f, channel.Meter.ReadAndReset().Max);
    }

    [Fact]
    public void Rejects_sources_not_in_the_mix_format()
    {
        var mono = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        Assert.Throws<ArgumentException>(() => new MixerChannel("bad", new ConstantSource(0f, mono)));
    }
}
