using NAudio.Wave;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public class RecorderTests
{
    [Fact]
    public async Task Writes_everything_received_while_recording()
    {
        var recorder = new Recorder();
        var encoder = new CapturingEncoder();
        recorder.Start("episode.raw", encoder);

        recorder.Write(Enumerable.Repeat(0.1f, 96_000).ToArray());
        var path = await recorder.StopAsync();

        Assert.Equal("episode.raw", path);
        Assert.Equal(96_000, encoder.Samples.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), recorder.Elapsed);
        Assert.True(encoder.Disposed);
        Assert.Equal(RecorderState.Idle, recorder.State);
    }

    [Fact]
    public async Task Paused_audio_is_not_written()
    {
        var recorder = new Recorder();
        var encoder = new CapturingEncoder();
        recorder.Start("episode.raw", encoder);

        recorder.Write(new float[960]);
        recorder.Pause();
        recorder.Write(new float[960]);
        recorder.Resume();
        recorder.Write(new float[960]);
        await recorder.StopAsync();

        Assert.Equal(1920, encoder.Samples.Count);
    }

    [Fact]
    public void Cannot_start_twice()
    {
        var recorder = new Recorder();
        recorder.Start("a.raw", new CapturingEncoder());

        Assert.Throws<InvalidOperationException>(() => recorder.Start("b.raw", new CapturingEncoder()));
    }

    [Fact]
    public async Task Wav_encoder_writes_a_readable_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"podcastrecorder-{Guid.NewGuid():N}.wav");
        try
        {
            var recorder = new Recorder();
            recorder.Start(path, new WavEncoder());
            recorder.Write(Enumerable.Repeat(0.5f, 48_000).ToArray());
            await recorder.StopAsync();

            using var reader = new WaveFileReader(path);
            Assert.Equal(AudioFormat.SampleRate, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.Equal(TimeSpan.FromSeconds(0.5), reader.TotalTime);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Catalog_falls_back_to_default_for_unknown_ids()
    {
        Assert.Equal(EncoderCatalog.Default, EncoderCatalog.Find("ogg"));
        Assert.Equal("wav", EncoderCatalog.Find("wav").Id);
    }
}
