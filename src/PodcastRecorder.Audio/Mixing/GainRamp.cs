namespace PodcastRecorder.Audio.Mixing;

/// <summary>
/// Applies a gain to interleaved samples, ramping linearly across the block when the
/// target changes so volume moves and mutes do not click.
/// </summary>
internal sealed class GainRamp(float initial)
{
    private float _current = initial;

    public float Current => _current;

    public void Apply(Span<float> samples, int channels, float target)
    {
        int frames = samples.Length / channels;
        if (frames == 0)
        {
            return;
        }

        if (_current == target)
        {
            if (target == 1f) return;
            if (target == 0f)
            {
                samples.Clear();
                return;
            }

            for (int i = 0; i < samples.Length; i++) samples[i] *= target;
            return;
        }

        float step = (target - _current) / frames;
        float gain = _current;
        for (int f = 0; f < frames; f++)
        {
            gain += step;
            for (int c = 0; c < channels; c++) samples[f * channels + c] *= gain;
        }

        _current = target;
    }
}
