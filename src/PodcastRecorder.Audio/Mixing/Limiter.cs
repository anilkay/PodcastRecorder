namespace PodcastRecorder.Audio.Mixing;

/// <summary>
/// A stereo-linked peak limiter for the program bus. Gain drops instantly when a peak would
/// pass the ceiling and recovers over <see cref="ReleaseTime"/>, so the recording never clips.
/// </summary>
public sealed class Limiter
{
    private readonly Lock _gate = new();
    private float _gain = 1f;
    private float _ceiling = DecibelsToGain(-1);
    private float _releaseCoefficient;
    private float _lowestGain = 1f;

    public Limiter()
    {
        ReleaseTime = TimeSpan.FromMilliseconds(150);
    }

    public bool Enabled { get; set; } = true;

    public double CeilingDb
    {
        get => 20 * Math.Log10(_ceiling);
        set => _ceiling = DecibelsToGain(Math.Clamp(value, -12, 0));
    }

    public TimeSpan ReleaseTime
    {
        get;
        set
        {
            field = value;
            _releaseCoefficient = (float)Math.Exp(-1.0 / (value.TotalSeconds * AudioFormat.SampleRate));
        }
    }

    /// <summary>The deepest gain reduction since the last call, in dB (0 or negative).</summary>
    public double ReadAndResetReductionDb()
    {
        lock (_gate)
        {
            double db = 20 * Math.Log10(_lowestGain);
            _lowestGain = _gain;
            return db;
        }
    }

    public void Process(Span<float> samples, int channels)
    {
        if (!Enabled)
        {
            _gain = 1f;
            return;
        }

        float lowest = 1f;
        for (int i = 0; i + channels <= samples.Length; i += channels)
        {
            float peak = 0f;
            for (int c = 0; c < channels; c++) peak = Math.Max(peak, Math.Abs(samples[i + c]));

            float released = 1f - (1f - _gain) * _releaseCoefficient;
            float allowed = peak > _ceiling ? _ceiling / peak : 1f;
            _gain = Math.Min(released, allowed);
            lowest = Math.Min(lowest, _gain);

            for (int c = 0; c < channels; c++) samples[i + c] *= _gain;
        }

        lock (_gate) _lowestGain = Math.Min(_lowestGain, lowest);
    }

    private static float DecibelsToGain(double db) => (float)Math.Pow(10, db / 20);
}
