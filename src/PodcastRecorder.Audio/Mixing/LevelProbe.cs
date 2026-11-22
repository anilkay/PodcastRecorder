namespace PodcastRecorder.Audio.Mixing;

/// <summary>What a <see cref="LevelProbe"/> heard: the RMS of each 50 ms window and the highest sample.</summary>
public sealed record LevelReading(IReadOnlyList<float> WindowRms, float Peak)
{
    public static LevelReading Empty { get; } = new([], 0f);
}

/// <summary>
/// Collects a channel's level in short windows for the pre-recording check. The audio thread
/// calls <see cref="Process"/>; the UI takes what was collected with <see cref="TakeReading"/>.
/// </summary>
public sealed class LevelProbe
{
    /// <summary>Length of one measuring window.</summary>
    public static readonly TimeSpan WindowLength = TimeSpan.FromMilliseconds(50);

    private const int WindowFrames = AudioFormat.SampleRate / 20;

    private readonly Lock _gate = new();

    // Room for a minute of windows, so the audio thread does not allocate during a check.
    private readonly List<float> _windows = new(1200);
    private double _sumOfSquares;
    private int _samplesInWindow;
    private int _framesInWindow;
    private float _peak;

    public void Process(ReadOnlySpan<float> samples, int channels)
    {
        lock (_gate)
        {
            for (int i = 0; i + channels <= samples.Length; i += channels)
            {
                for (int c = 0; c < channels; c++)
                {
                    float s = samples[i + c];
                    _sumOfSquares += s * s;
                    float abs = Math.Abs(s);
                    if (abs > _peak) _peak = abs;
                }

                _samplesInWindow += channels;
                if (++_framesInWindow == WindowFrames)
                {
                    if (_windows.Count < _windows.Capacity) _windows.Add((float)Math.Sqrt(_sumOfSquares / _samplesInWindow));
                    _sumOfSquares = 0;
                    _samplesInWindow = 0;
                    _framesInWindow = 0;
                }
            }
        }
    }

    /// <summary>Returns everything measured since the last call and starts over.</summary>
    public LevelReading TakeReading()
    {
        lock (_gate)
        {
            var reading = new LevelReading([.. _windows], _peak);
            _windows.Clear();
            _sumOfSquares = 0;
            _samplesInWindow = 0;
            _framesInWindow = 0;
            _peak = 0f;
            return reading;
        }
    }
}
