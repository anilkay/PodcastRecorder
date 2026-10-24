using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public class AudioMixerTests
{
    [Fact]
    public void Program_bus_has_every_channel_and_monitor_skips_microphones()
    {
        var mixer = new AudioMixer();
        mixer.Add(new MixerChannel("mic", new ConstantSource(0.25f), monitorEnabled: false));
        mixer.Add(new MixerChannel("music", new ConstantSource(0.5f)));
        var program = new float[480];
        var monitor = new float[480];

        mixer.Mix(program, monitor);

        Assert.All(program, s => Assert.Equal(0.75f, s, 5));
        Assert.All(monitor, s => Assert.Equal(0.5f, s, 5));
    }

    [Fact]
    public void Removed_channel_is_no_longer_mixed()
    {
        var mixer = new AudioMixer();
        var channel = new MixerChannel("music", new ConstantSource(0.5f));
        mixer.Add(channel);
        mixer.Remove(channel);
        var program = new float[480];

        mixer.Mix(program, new float[480]);

        Assert.All(program, s => Assert.Equal(0f, s));
    }

    [Fact]
    public async Task Output_bus_records_program_and_plays_monitor()
    {
        var mixer = new AudioMixer();
        mixer.Add(new MixerChannel("mic", new ConstantSource(0.25f), monitorEnabled: false));
        mixer.Add(new MixerChannel("music", new ConstantSource(0.5f)));
        var recorder = new Recorder();
        var encoder = new CapturingEncoder();
        recorder.Start("out.raw", encoder);
        var bus = new OutputBus(mixer, recorder);
        var output = new float[480];

        bus.Read(output, 0, output.Length);
        await recorder.StopAsync();

        Assert.All(output, s => Assert.Equal(0.5f, s, 5));
        Assert.Equal(480, encoder.Samples.Count);
        Assert.All(encoder.Samples, s => Assert.Equal(0.75f, s, 5));
    }
}
