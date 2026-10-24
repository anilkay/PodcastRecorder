using NAudio.Wave;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public class WmaEncoderTests
{
    [Fact]
    public async Task Wma_encoder_writes_a_readable_file_and_removes_the_temporary_wav()
    {
        var folder = Directory.CreateTempSubdirectory("podcastrecorder-");
        try
        {
            var path = Path.Combine(folder.FullName, "episode.wma");
            var recorder = new Recorder();
            recorder.Start(path, new WmaEncoder(128));
            recorder.Write(Enumerable.Repeat(0.25f, AudioFormat.SampleRate * AudioFormat.Channels).ToArray());
            await recorder.StopAsync();

            Assert.Equal([path], Directory.GetFiles(folder.FullName));
            using var reader = new MediaFoundationReader(path);
            Assert.Equal(AudioFormat.SampleRate, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.InRange(reader.TotalTime.TotalSeconds, 0.9, 1.2);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Wma_recording_started_on_the_ui_thread_can_be_closed_in_the_background()
    {
        // The app opens the encoder on the WPF (STA) thread and closes it on the thread pool (MTA).
        if (!OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateTempSubdirectory("podcastrecorder-");
        try
        {
            var path = Path.Combine(folder.FullName, "episode.wma");
            var recorder = new Recorder();
            var ui = new Thread(() =>
            {
                recorder.Start(path, new WmaEncoder(128));
                recorder.Write(new float[AudioFormat.SampleRate * AudioFormat.Channels]);
            });
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            ui.Join();

            await Task.Run(recorder.StopAsync);

            Assert.True(new FileInfo(path).Length > 0);
            Assert.Equal([path], Directory.GetFiles(folder.FullName));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void Catalog_offers_wma()
    {
        Assert.Equal(".wma", EncoderCatalog.Find("wma-192").Create().FileExtension);
    }
}
