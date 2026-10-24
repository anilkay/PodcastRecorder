using NAudio.Wave;

namespace PodcastRecorder.Audio;

/// <summary>
/// The single internal format every source is converted to before it reaches the mixer.
/// </summary>
public static class AudioFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    public static WaveFormat Mix { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    public static bool IsMixFormat(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.IeeeFloat
        && format.SampleRate == SampleRate
        && format.Channels == Channels;

    public static TimeSpan FramesToTime(long frames) =>
        TimeSpan.FromSeconds((double)frames / SampleRate);
}
