using System.Buffers;
using System.Threading.Channels;
using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.Audio.Recording;

public enum RecorderState
{
    Idle,
    Recording,
    Paused,
}

/// <summary>
/// Receives the program bus on the audio thread and hands it to the encoder on a background
/// task, so slow encoding or disk writes never stall playback.
/// </summary>
public sealed class Recorder : IProgramSink
{
    private readonly Lock _gate = new();
    private Channel<Block>? _queue;
    private Task? _writer;
    private IAudioEncoder? _encoder;
    private long _frames;
    private volatile RecorderState _state = RecorderState.Idle;

    public RecorderState State => _state;

    public string? OutputPath { get; private set; }

    /// <summary>Length of audio written so far, excluding paused time.</summary>
    public TimeSpan Elapsed => AudioFormat.FramesToTime(Interlocked.Read(ref _frames));

    /// <summary>Raised on a background thread if the encoder fails; recording stops writing.</summary>
    public event EventHandler<Exception>? Faulted;

    public void Start(string path, IAudioEncoder encoder)
    {
        lock (_gate)
        {
            if (_state != RecorderState.Idle)
            {
                throw new InvalidOperationException("A recording is already running.");
            }

            encoder.Open(path, AudioFormat.Mix);
            var queue = Channel.CreateUnbounded<Block>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            _encoder = encoder;
            _queue = queue;
            _writer = Task.Run(() => WriteLoopAsync(queue.Reader, encoder));
            OutputPath = path;
            Interlocked.Exchange(ref _frames, 0);
            _state = RecorderState.Recording;
        }
    }

    public void Pause()
    {
        if (_state == RecorderState.Recording) _state = RecorderState.Paused;
    }

    public void Resume()
    {
        if (_state == RecorderState.Paused) _state = RecorderState.Recording;
    }

    /// <summary>Stops recording, waits for queued audio to be written and closes the file.</summary>
    public async Task<string?> StopAsync()
    {
        Task? writer;
        IAudioEncoder? encoder;
        lock (_gate)
        {
            if (_state == RecorderState.Idle)
            {
                return null;
            }

            _state = RecorderState.Idle;
            _queue?.Writer.TryComplete();
            _queue = null;
            writer = _writer;
            encoder = _encoder;
            _writer = null;
            _encoder = null;
        }

        if (writer is not null) await writer.ConfigureAwait(false);
        encoder?.Dispose();
        return OutputPath;
    }

    /// <summary>Called by the output bus with each block of the program mix.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        var queue = _queue;
        if (_state != RecorderState.Recording || queue is null || samples.IsEmpty)
        {
            return;
        }

        var buffer = ArrayPool<float>.Shared.Rent(samples.Length);
        samples.CopyTo(buffer);
        if (queue.Writer.TryWrite(new Block(buffer, samples.Length)))
        {
            Interlocked.Add(ref _frames, samples.Length / AudioFormat.Channels);
        }
        else
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    private async Task WriteLoopAsync(ChannelReader<Block> reader, IAudioEncoder encoder)
    {
        bool failed = false;
        await foreach (var block in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (!failed) encoder.Write(block.Samples, 0, block.Length);
            }
            catch (Exception ex)
            {
                failed = true;
                Faulted?.Invoke(this, ex);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(block.Samples);
            }
        }
    }

    private readonly record struct Block(float[] Samples, int Length);
}
