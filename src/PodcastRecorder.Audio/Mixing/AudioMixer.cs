namespace PodcastRecorder.Audio.Mixing;

/// <summary>Receives each channel's post-fader block during a mix, e.g. for multitrack recording.</summary>
public delegate void ChannelSink(MixerChannel channel, ReadOnlySpan<float> samples);

/// <summary>
/// Sums all channels into two buses: the program bus (what gets recorded) and the
/// monitor bus (what the host hears).
/// </summary>
public sealed class AudioMixer
{
    private readonly Lock _gate = new();
    private readonly List<MixerChannel> _channels = [];
    private readonly GainRamp _masterGain = new(1f);
    private readonly GainRamp _monitorGain = new(1f);
    private float[] _scratch = [];
    private float _masterVolume = 1f;
    private float _monitorVolume = 1f;

    public IReadOnlyList<MixerChannel> Channels
    {
        get
        {
            lock (_gate) return _channels.ToArray();
        }
    }

    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, 0f, MixerChannel.MaxVolume);
    }

    public float MonitorVolume
    {
        get => _monitorVolume;
        set => _monitorVolume = Math.Clamp(value, 0f, MixerChannel.MaxVolume);
    }

    /// <summary>Meter of the program bus after the master fader.</summary>
    public PeakMeter MasterMeter { get; } = new();

    public void Add(MixerChannel channel)
    {
        lock (_gate) _channels.Add(channel);
    }

    public bool Remove(MixerChannel channel)
    {
        lock (_gate) return _channels.Remove(channel);
    }

    /// <summary>
    /// Fills both buses with the same number of interleaved samples. Channels that run short
    /// are padded with silence, so the mixer always produces a continuous stream.
    /// </summary>
    public void Mix(Span<float> program, Span<float> monitor, ChannelSink? channelSink = null)
    {
        if (program.Length != monitor.Length)
        {
            throw new ArgumentException("Program and monitor buffers must be the same length.");
        }

        program.Clear();
        monitor.Clear();

        int count = program.Length;
        if (_scratch.Length < count)
        {
            _scratch = new float[count];
        }

        MixerChannel[] channels;
        lock (_gate) channels = _channels.ToArray();

        foreach (var channel in channels)
        {
            int read = channel.Read(_scratch, 0, count);
            if (channelSink is not null)
            {
                _scratch.AsSpan(read, count - read).Clear();
                channelSink(channel, _scratch.AsSpan(0, count));
            }

            var source = _scratch.AsSpan(0, read);
            for (int i = 0; i < read; i++) program[i] += source[i];
            if (channel.MonitorEnabled)
            {
                for (int i = 0; i < read; i++) monitor[i] += source[i];
            }
        }

        _masterGain.Apply(program, AudioFormat.Channels, _masterVolume);
        _monitorGain.Apply(monitor, AudioFormat.Channels, _monitorVolume);
        MasterMeter.Process(program, AudioFormat.Channels);
    }
}
