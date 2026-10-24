using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public class MultitrackTests
{
    [Fact]
    public async Task Each_channel_is_recorded_to_its_own_file_next_to_the_mix()
    {
        using var engine = new AudioEngine();
        var guest = engine.AddMicrophone(new ConstantSource(0.25f), "Konuk");
        var host = engine.AddMicrophone(new ConstantSource(0.125f), "Sunucu");
        var mix = new CapturingEncoder();
        var guestTrack = new CapturingEncoder();
        var hostTrack = new CapturingEncoder();
        engine.Recorder.Start(
            new RecordingTarget("mix.raw", mix),
            new Dictionary<MixerChannel, RecordingTarget>
            {
                [guest] = new("guest.raw", guestTrack),
                [host] = new("host.raw", hostTrack),
            });

        engine.Bus.Read(new float[960], 0, 960);
        await engine.Recorder.StopAsync();

        Assert.All(mix.Samples, s => Assert.Equal(0.375f, s, 5));
        Assert.All(guestTrack.Samples, s => Assert.Equal(0.25f, s, 5));
        Assert.All(hostTrack.Samples, s => Assert.Equal(0.125f, s, 5));
        Assert.Equal(960, guestTrack.Samples.Count);
        Assert.Equal(["guest.raw", "host.raw"], engine.Recorder.StemPaths);
        Assert.True(guestTrack.Disposed && hostTrack.Disposed && mix.Disposed);
    }

    [Fact]
    public void Failing_to_open_a_track_closes_the_ones_already_opened()
    {
        var recorder = new Recorder();
        var mix = new CapturingEncoder();
        var channel = new MixerChannel("x", new ConstantSource(0f));

        Assert.Throws<IOException>(() => recorder.Start(
            new RecordingTarget("mix.raw", mix),
            new Dictionary<MixerChannel, RecordingTarget> { [channel] = new("bad.raw", new FailingEncoder()) }));

        Assert.True(mix.Disposed);
        Assert.Equal(RecorderState.Idle, recorder.State);
    }

    private sealed class FailingEncoder : IAudioEncoder
    {
        public string FileExtension => ".raw";

        public void Open(string path, NAudio.Wave.WaveFormat inputFormat) => throw new IOException("disk full");

        public void Write(float[] samples, int offset, int count)
        {
        }

        public void Dispose()
        {
        }
    }
}
