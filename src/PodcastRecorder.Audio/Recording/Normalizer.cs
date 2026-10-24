using NAudio.Wave;
using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// Brings a finished recording to a target loudness: reads the float WAV captured during
/// recording, applies one fixed gain (with the limiter catching any new peaks) and encodes
/// the result in the chosen format.
/// </summary>
public static class Normalizer
{
    public const double PodcastTargetLufs = -16;

    /// <summary>Gain in dB to move <paramref name="measuredLufs"/> to the target, limited to ±20 dB.</summary>
    public static double GainFor(double measuredLufs, double targetLufs = PodcastTargetLufs) =>
        double.IsFinite(measuredLufs) ? Math.Clamp(targetLufs - measuredLufs, -20, 20) : 0;

    public static Task NormalizeAsync(
        string sourceWav, RecordingTarget output, double gainDb, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Normalize(sourceWav, output, gainDb, progress, cancellationToken), cancellationToken);

    private static void Normalize(
        string sourceWav, RecordingTarget output, double gainDb, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var reader = new WaveFileReader(sourceWav);
        var samples = reader.ToSampleProvider();
        if (!AudioFormat.IsMixFormat(samples.WaveFormat))
        {
            throw new InvalidOperationException($"Expected {AudioFormat.Mix}, got {samples.WaveFormat}.");
        }

        float gain = (float)Math.Pow(10, gainDb / 20);
        var limiter = new Limiter();
        var buffer = new float[AudioFormat.SampleRate * AudioFormat.Channels];
        long total = reader.SampleCount * AudioFormat.Channels;
        long done = 0;

        using var encoder = output.Encoder;
        encoder.Open(output.Path, AudioFormat.Mix);
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = buffer.AsSpan(0, read);
            for (int i = 0; i < block.Length; i++) block[i] *= gain;
            limiter.Process(block, AudioFormat.Channels);
            encoder.Write(buffer, 0, read);
            done += read;
            progress?.Report(total > 0 ? (double)done / total : 1);
        }
    }
}
