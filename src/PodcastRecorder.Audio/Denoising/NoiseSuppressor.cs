namespace PodcastRecorder.Audio.Denoising;

/// <summary>
/// Removes steady background noise (hum, fans, keyboard clatter) from interleaved 48 kHz audio
/// with RNNoise, one model state per channel. While it is on the output runs exactly
/// <see cref="LatencyFrames"/> frames (20 ms) behind the input. While it is off the audio passes
/// straight through with no delay; switching it on mid-stream replays the last 10 ms once
/// instead of leaving a gap.
/// </summary>
public sealed class NoiseSuppressor : IDisposable
{
    /// <summary>
    /// The fixed delay between input and output while enabled, in frames: 10 ms of buffering
    /// (RNNoise takes whole frames) plus RNNoise's own 10 ms overlap-add window.
    /// </summary>
    public const int LatencyFrames = 2 * RnnoiseNative.FrameSize;

    private const int BufferFrames = RnnoiseNative.FrameSize;

    private readonly int _channels;
    private readonly nint[] _states;
    private readonly float[] _pending;   // interleaved input not yet processed
    private readonly float[] _ready;     // interleaved output not yet handed out (ring)
    private readonly float[] _frameIn = new float[RnnoiseNative.FrameSize];
    private readonly float[] _frameOut = new float[RnnoiseNative.FrameSize];
    private readonly float[] _history;   // the last BufferFrames of input while bypassed
    private readonly Lock _gate = new();
    private bool _active;
    private int _pendingFrames;
    private int _readyStart;
    private int _readyFrames;
    private bool _disposed;

    public NoiseSuppressor(int channels)
    {
        if (!IsAvailable) throw new PlatformNotSupportedException(Texts.Get("Denoise_Unavailable"));
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        _channels = channels;
        _states = new nint[channels];
        for (int c = 0; c < channels; c++) _states[c] = RnnoiseNative.rnnoise_create(0);
        _pending = new float[RnnoiseNative.FrameSize * channels];
        _ready = new float[RnnoiseNative.FrameSize * 4 * channels];
        _history = new float[BufferFrames * channels];
    }

    /// <summary>Whether this system has the RNNoise library (Windows and Linux do; macOS not yet).</summary>
    public static bool IsAvailable => RnnoiseNative.IsAvailable;

    /// <summary>Off passes the audio straight through, untouched and without delay.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Pushes <paramref name="input"/> in and fills <paramref name="output"/> with as many
    /// samples, delayed by <see cref="LatencyFrames"/> while enabled. Both are interleaved, whole frames.
    /// </summary>
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        // The audio thread may still be in here when the channel is removed and disposed.
        lock (_gate) ProcessLocked(input, output);
    }

    private void ProcessLocked(ReadOnlySpan<float> input, Span<float> output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (output.Length != input.Length) throw new ArgumentException("Input and output must be the same length.");

        if (!Enabled)
        {
            _active = false;
            input.CopyTo(output);
            Remember(input);
            return;
        }

        if (!_active)
        {
            // Queue one frame (the audio just heard) ahead: from then on at least as much is ready as asked for.
            _active = true;
            _pendingFrames = 0;
            _readyStart = 0;
            _history.CopyTo(_ready, 0);
            _readyFrames = BufferFrames;
        }

        int frames = input.Length / _channels;
        int written = 0;
        for (int f = 0; f < frames; f++)
        {
            input.Slice(f * _channels, _channels).CopyTo(_pending.AsSpan(_pendingFrames * _channels));
            if (++_pendingFrames == RnnoiseNative.FrameSize)
            {
                RunFrame();
                _pendingFrames = 0;
            }

            // Hand out as we go so the ring never holds more than a frame and a bit.
            written += Drain(output[(written * _channels)..], f + 1 - written);
        }
    }

    private void Remember(ReadOnlySpan<float> input)
    {
        if (input.Length >= _history.Length)
        {
            input[^_history.Length..].CopyTo(_history);
            return;
        }

        _history.AsSpan(input.Length).CopyTo(_history);
        input.CopyTo(_history.AsSpan(_history.Length - input.Length));
    }

    private int Drain(Span<float> output, int wanted)
    {
        int count = Math.Min(wanted, _readyFrames);
        int capacity = _ready.Length / _channels;
        for (int i = 0; i < count; i++)
        {
            int slot = (_readyStart + i) % capacity;
            _ready.AsSpan(slot * _channels, _channels).CopyTo(output.Slice(i * _channels, _channels));
        }

        _readyStart = (_readyStart + count) % capacity;
        _readyFrames -= count;
        return count;
    }

    private unsafe void RunFrame()
    {
        int capacity = _ready.Length / _channels;
        int start = (_readyStart + _readyFrames) % capacity;
        for (int c = 0; c < _channels; c++)
        {
            for (int i = 0; i < RnnoiseNative.FrameSize; i++) _frameIn[i] = _pending[i * _channels + c] * RnnoiseNative.Scale;
            fixed (float* pin = _frameIn, pout = _frameOut)
            {
                RnnoiseNative.rnnoise_process_frame(_states[c], pout, pin);
            }

            for (int i = 0; i < RnnoiseNative.FrameSize; i++)
            {
                _frameOut[i] = Math.Clamp(_frameOut[i] / RnnoiseNative.Scale, -1f, 1f);
            }

            for (int i = 0; i < RnnoiseNative.FrameSize; i++)
            {
                _ready[((start + i) % capacity) * _channels + c] = _frameOut[i];
            }
        }

        _readyFrames += RnnoiseNative.FrameSize;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var state in _states) RnnoiseNative.rnnoise_destroy(state);
        }
    }
}
