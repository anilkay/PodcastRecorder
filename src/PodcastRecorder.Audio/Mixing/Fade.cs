namespace PodcastRecorder.Audio.Mixing;

/// <summary>
/// A gain that moves to a target over a set time, frame by frame. Used for ducking,
/// microphone on/off and crossfades, where the change must be audible as a smooth fade.
/// </summary>
internal sealed class Fade(float initial = 1f)
{
    private float _step;

    public float Current { get; private set; } = initial;

    public float Target { get; private set; } = initial;

    public bool IsComplete => Current == Target;

    public void Start(float target, TimeSpan duration)
    {
        Target = target;
        double frames = duration.TotalSeconds * AudioFormat.SampleRate;
        _step = frames < 1 ? float.PositiveInfinity : (float)(Math.Abs(target - Current) / frames);
        if (float.IsPositiveInfinity(_step)) Current = target;
    }

    public void Apply(Span<float> samples, int channels)
    {
        if (IsComplete)
        {
            if (Current == 1f) return;
            if (Current == 0f)
            {
                samples.Clear();
                return;
            }

            for (int i = 0; i < samples.Length; i++) samples[i] *= Current;
            return;
        }

        int frames = samples.Length / channels;
        for (int f = 0; f < frames; f++)
        {
            Current = Current < Target
                ? Math.Min(Current + _step, Target)
                : Math.Max(Current - _step, Target);
            for (int c = 0; c < channels; c++) samples[f * channels + c] *= Current;
        }
    }
}
