using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.Audio.Tests;

public class LoudnessTests
{
    [Theory]
    [InlineData(-23.0)]
    [InlineData(-33.0)]
    public void Stereo_1kHz_sine_reads_its_level_in_LUFS(double dbfs)
    {
        // EBU Tech 3341 test cases 1 and 2.
        var meter = new LoudnessMeter();
        meter.Process(StereoSine(1000, dbfs, seconds: 20), integrate: true);

        Assert.Equal(dbfs, meter.MomentaryLufs, 1);
        Assert.Equal(dbfs, meter.ShortTermLufs, 1);
        Assert.Equal(dbfs, meter.IntegratedLufs, 1);
    }

    [Fact]
    public void Integrated_value_gates_out_quiet_passages()
    {
        // EBU Tech 3341 test case 3: -36, -23, -36 dBFS for 10, 60, 10 s reads -23 LUFS.
        var meter = new LoudnessMeter();
        meter.Process(StereoSine(1000, -36, 10), integrate: true);
        meter.Process(StereoSine(1000, -23, 60), integrate: true);
        meter.Process(StereoSine(1000, -36, 10), integrate: true);

        Assert.Equal(-23.0, meter.IntegratedLufs, 1);
    }

    [Fact]
    public void Only_integrated_audio_counts_and_reset_clears_it()
    {
        var meter = new LoudnessMeter();
        meter.Process(StereoSine(1000, -23, 5), integrate: false);
        Assert.Equal(double.NegativeInfinity, meter.IntegratedLufs);

        meter.Process(StereoSine(1000, -23, 5), integrate: true);
        meter.ResetIntegrated();
        Assert.Equal(double.NegativeInfinity, meter.IntegratedLufs);
    }

    [Fact]
    public void Limiter_keeps_peaks_under_the_ceiling()
    {
        var limiter = new Limiter { CeilingDb = -1 };
        var samples = StereoSine(200, 6, seconds: 1);

        limiter.Process(samples, 2);

        float ceiling = (float)Math.Pow(10, -1 / 20.0);
        Assert.All(samples, s => Assert.True(Math.Abs(s) <= ceiling + 1e-6f));
        Assert.True(limiter.ReadAndResetReductionDb() < -6);
    }

    [Fact]
    public void Limiter_leaves_quiet_audio_untouched()
    {
        var limiter = new Limiter();
        var samples = StereoSine(200, -6, seconds: 1);
        var original = (float[])samples.Clone();

        limiter.Process(samples, 2);

        Assert.Equal(original, samples);
        Assert.Equal(0, limiter.ReadAndResetReductionDb());
    }

    private static float[] StereoSine(double hz, double dbfs, double seconds)
    {
        int frames = (int)(seconds * AudioFormat.SampleRate);
        double amplitude = Math.Pow(10, dbfs / 20);
        var samples = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float value = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / AudioFormat.SampleRate));
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        return samples;
    }
}
