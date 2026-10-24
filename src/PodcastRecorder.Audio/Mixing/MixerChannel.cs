using NAudio.Wave;

namespace PodcastRecorder.Audio.Mixing;

/// <summary>
/// One input strip of the mixer: a source plus its fader, mute and meter.
/// </summary>
public sealed class MixerChannel : ISampleProvider
{
    public const float MaxVolume = 2f;

    private readonly ISampleProvider _source;
    private readonly GainRamp _gain;
    private float _volume = 1f;

    public MixerChannel(string name, ISampleProvider source, bool monitorEnabled = true)
    {
        if (!AudioFormat.IsMixFormat(source.WaveFormat))
        {
            throw new ArgumentException(
                $"Source must be in the mix format ({AudioFormat.Mix}); got {source.WaveFormat}.", nameof(source));
        }

        Name = name;
        _source = source;
        MonitorEnabled = monitorEnabled;
        _gain = new GainRamp(_volume);
    }

    public string Name { get; set; }

    public ISampleProvider Source => _source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Linear gain, 0 to <see cref="MaxVolume"/>.</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, MaxVolume);
    }

    public bool IsMuted { get; set; }

    /// <summary>
    /// Whether the channel is heard on the monitor output. Microphones default to off so the
    /// speaker does not hear their own delayed voice; they are always in the recording.
    /// </summary>
    public bool MonitorEnabled { get; set; }

    public PeakMeter Meter { get; } = new();

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        var samples = buffer.AsSpan(offset, read);
        _gain.Apply(samples, WaveFormat.Channels, IsMuted ? 0f : _volume);
        Meter.Process(samples, WaveFormat.Channels);
        return read;
    }
}
