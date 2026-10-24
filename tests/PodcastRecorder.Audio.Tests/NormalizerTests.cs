using NAudio.Wave;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public class NormalizerTests
{
    [Fact]
    public async Task Quiet_recording_is_brought_to_the_target_loudness()
    {
        var source = TempPath();
        var output = TempPath();
        try
        {
            using (var encoder = new WavEncoder(floatingPoint: true))
            {
                encoder.Open(source, AudioFormat.Mix);
                var sine = Sine(-30, seconds: 5);
                encoder.Write(sine, 0, sine.Length);
            }

            double gain = Normalizer.GainFor(-30);
            await Normalizer.NormalizeAsync(source, new RecordingTarget(output, new WavEncoder()), gain);

            Assert.Equal(14, gain);
            var meter = new LoudnessMeter();
            using var reader = new WaveFileReader(output);
            var samples = new float[reader.SampleCount * 2];
            reader.ToSampleProvider().Read(samples, 0, samples.Length);
            meter.Process(samples, integrate: true);
            Assert.Equal(-16, meter.IntegratedLufs, 0);
        }
        finally
        {
            File.Delete(source);
            File.Delete(output);
        }
    }

    [Fact]
    public void Gain_is_limited_and_silence_is_left_alone()
    {
        Assert.Equal(20, Normalizer.GainFor(-60));
        Assert.Equal(-4, Normalizer.GainFor(-12));
        Assert.Equal(0, Normalizer.GainFor(double.NegativeInfinity));
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"podcastrecorder-{Guid.NewGuid():N}.wav");

    private static float[] Sine(double dbfs, double seconds)
    {
        int frames = (int)(seconds * AudioFormat.SampleRate);
        double amplitude = Math.Pow(10, dbfs / 20);
        var samples = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            samples[i * 2] = samples[i * 2 + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * 1000 * i / AudioFormat.SampleRate));
        }

        return samples;
    }
}
