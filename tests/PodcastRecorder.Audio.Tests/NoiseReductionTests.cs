using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PodcastRecorder.Audio.Denoising;

namespace PodcastRecorder.Audio.Tests;

public class NoiseReductionTests
{
    private const int Rate = AudioFormat.SampleRate;

    [Fact]
    public void RNNoise_is_available_on_windows_and_linux()
    {
        if (OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture is System.Runtime.InteropServices.Architecture.X64 or System.Runtime.InteropServices.Architecture.Arm64))
        {
            Assert.True(NoiseSuppressor.IsAvailable);
        }
    }

    [Fact]
    public void Steady_background_noise_is_strongly_reduced()
    {
        if (!NoiseSuppressor.IsAvailable) return;
        var noise = Noise(seconds: 3, amplitude: 0.05f);
        using var suppressor = new NoiseSuppressor(channels: 2);
        var output = new float[noise.Length];
        suppressor.Process(noise, output);

        // Skip the first second while the model settles.
        double before = Rms(noise.AsSpan(Rate * 2));
        double after = Rms(output.AsSpan(Rate * 2));
        Assert.True(after < before * 0.25, $"RMS {before:0.0000} → {after:0.0000}");
    }

    [Fact]
    public void Switched_off_it_passes_audio_through_untouched_and_on_time()
    {
        if (!NoiseSuppressor.IsAvailable) return;
        var input = Noise(seconds: 0.2, amplitude: 0.3f);
        using var suppressor = new NoiseSuppressor(channels: 2) { Enabled = false };
        var output = new float[input.Length];
        // Odd block sizes, like an audio device asks for.
        for (int at = 0; at < input.Length;)
        {
            int n = Math.Min(2 * 333, input.Length - at);
            suppressor.Process(input.AsSpan(at, n), output.AsSpan(at, n));
            at += n;
        }

        Assert.Equal(input, output);
    }

    [Fact]
    public void A_cleaned_file_keeps_the_original_length_and_timing()
    {
        if (!NoiseSuppressor.IsAvailable) return;
        // A loud click at 0.5 s in 1.234 s of quiet.
        int frames = (int)(Rate * 1.234);
        var samples = new float[frames * 2];
        int click = Rate / 2;
        for (int i = 0; i < 480; i++)
        {
            float v = (float)Math.Sin(2 * Math.PI * 300 * i / Rate) * 0.8f;
            samples[(click + i) * 2] = v;
            samples[(click + i) * 2 + 1] = v;
        }

        using var cleaned = new DenoisedFile(new ArraySource(samples));
        var output = ReadAll(cleaned);

        Assert.Equal(samples.Length, output.Length);
        int loudest = 0;
        for (int i = 0; i < output.Length; i += 2) if (Math.Abs(output[i]) > Math.Abs(output[loudest])) loudest = i;
        Assert.InRange(loudest / 2, click, click + 480);
    }

    [Fact]
    public void The_engine_can_switch_noise_reduction_on_a_live_microphone()
    {
        if (!NoiseSuppressor.IsAvailable) return;
        using var engine = new AudioEngine();
        var channel = engine.AddMicrophone(new ConstantSource(0.25f), "Mic");
        Assert.IsType<DenoisedSource>(channel.Source);
        engine.NoiseReductionEnabled = true;
        Assert.True(((DenoisedSource)channel.Source).Enabled);
        engine.NoiseReductionEnabled = false;
        Assert.False(((DenoisedSource)channel.Source).Enabled);
        engine.RemoveMicrophone(channel);
    }

private static float[] Noise(double seconds, float amplitude)
    {
        var random = new Random(42);
        var samples = new float[(int)(Rate * seconds) * 2];
        for (int i = 0; i < samples.Length; i += 2)
        {
            float v = (float)(random.NextDouble() * 2 - 1) * amplitude;
            samples[i] = v;
            samples[i + 1] = v;
        }

        return samples;
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return Math.Sqrt(sum / samples.Length);
    }

    private static float[] ReadAll(ISampleProvider source)
    {
        var all = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) all.AddRange(buffer.AsSpan(0, read));
        return [.. all];
    }

    private sealed class ArraySource(float[] samples) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat => AudioFormat.Mix;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, samples.Length - _position);
            samples.AsSpan(_position, n).CopyTo(buffer.AsSpan(offset));
            _position += n;
            return n;
        }
    }
}
