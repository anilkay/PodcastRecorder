namespace PodcastRecorder.Audio.Mixing;

public readonly record struct PeakLevel(float Left, float Right)
{
    public float Max => Math.Max(Left, Right);

    public bool IsClipping => Max >= 1f;
}

/// <summary>
/// Holds the highest sample seen since the UI last asked. The audio thread calls
/// <see cref="Process"/>, the UI polls <see cref="ReadAndReset"/> on a timer.
/// </summary>
public sealed class PeakMeter
{
    private readonly Lock _gate = new();
    private float _left;
    private float _right;

    public void Process(ReadOnlySpan<float> samples, int channels)
    {
        float left = 0f, right = 0f;
        for (int i = 0; i + channels <= samples.Length; i += channels)
        {
            float l = Math.Abs(samples[i]);
            float r = channels > 1 ? Math.Abs(samples[i + 1]) : l;
            if (l > left) left = l;
            if (r > right) right = r;
        }

        lock (_gate)
        {
            if (left > _left) _left = left;
            if (right > _right) _right = right;
        }
    }

    public PeakLevel ReadAndReset()
    {
        lock (_gate)
        {
            var level = new PeakLevel(_left, _right);
            _left = 0f;
            _right = 0f;
            return level;
        }
    }
}
