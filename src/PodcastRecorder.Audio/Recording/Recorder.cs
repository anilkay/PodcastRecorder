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

/// <summary>A file to record into and the encoder that writes it.</summary>
public sealed record RecordingTarget(string Path, IAudioEncoder Encoder);

/// <summary>
/// Receives the program bus (and optionally each channel on its own, for multitrack) on the
/// audio thread and hands it to the encoders on background tasks, so slow encoding or disk
/// writes never stall playback.
/// </summary>
public sealed class Recorder : IProgramSink
{
    private readonly Lock _gate = new();
    private Track? _mix;
    private Dictionary<MixerChannel, Track> _stems = [];
    private long _frames;
    private volatile RecorderState _state = RecorderState.Idle;

    public RecorderState State => _state;

    public string? OutputPath { get; private set; }

    /// <summary>Files of the separate channel tracks of the current or last recording.</summary>
    public IReadOnlyList<string> StemPaths { get; private set; } = [];

    public bool IsRecordingStems => _stems.Count > 0;

    /// <summary>Length of audio written so far, excluding paused time.</summary>
    public TimeSpan Elapsed => AudioFormat.FramesToTime(Interlocked.Read(ref _frames));

    /// <summary>Raised on a background thread if an encoder fails; that file stops being written.</summary>
    public event EventHandler<Exception>? Faulted;

    public void Start(string path, IAudioEncoder encoder) => Start(new RecordingTarget(path, encoder));

    /// <summary>
    /// Starts recording the mix, plus one file per channel given in <paramref name="stems"/>.
    /// </summary>
    public void Start(RecordingTarget mix, IReadOnlyDictionary<MixerChannel, RecordingTarget>? stems = null)
    {
        lock (_gate)
        {
            if (_state != RecorderState.Idle)
            {
                throw new InvalidOperationException("A recording is already running.");
            }

            var targets = new List<RecordingTarget> { mix };
            if (stems is not null) targets.AddRange(stems.Values);
            OpenAll(targets);

            _mix = new Track(mix, OnFaulted);
            _stems = stems?.ToDictionary(s => s.Key, s => new Track(s.Value, OnFaulted)) ?? [];
            OutputPath = mix.Path;
            StemPaths = stems?.Values.Select(t => t.Path).ToList() ?? [];
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

    /// <summary>Stops recording, waits for queued audio to be written and closes the files.</summary>
    public async Task<string?> StopAsync()
    {
        List<Track> tracks;
        lock (_gate)
        {
            if (_state == RecorderState.Idle)
            {
                return null;
            }

            _state = RecorderState.Idle;
            tracks = [.. _stems.Values];
            if (_mix is not null) tracks.Insert(0, _mix);
            _mix = null;
            _stems = [];
        }

        foreach (var track in tracks) track.Complete();
        foreach (var track in tracks) await track.CloseAsync().ConfigureAwait(false);
        return OutputPath;
    }

    /// <summary>Called by the output bus with each block of the program mix.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        var mix = _mix;
        if (_state != RecorderState.Recording || mix is null || samples.IsEmpty)
        {
            return;
        }

        if (mix.TryWrite(samples))
        {
            Interlocked.Add(ref _frames, samples.Length / AudioFormat.Channels);
        }
    }

    /// <summary>Called by the mixer with one channel's block, for multitrack recording.</summary>
    public void WriteStem(MixerChannel channel, ReadOnlySpan<float> samples)
    {
        if (_state == RecorderState.Recording && _stems.TryGetValue(channel, out var track))
        {
            track.TryWrite(samples);
        }
    }

    private static void OpenAll(List<RecordingTarget> targets)
    {
        var opened = new List<IAudioEncoder>();
        try
        {
            foreach (var target in targets)
            {
                target.Encoder.Open(target.Path, AudioFormat.Mix);
                opened.Add(target.Encoder);
            }
        }
        catch
        {
            foreach (var encoder in opened) encoder.Dispose();
            throw;
        }
    }

    private void OnFaulted(Exception ex) => Faulted?.Invoke(this, ex);

    /// <summary>One output file with its queue and writer task.</summary>
    private sealed class Track
    {
        private readonly Channel<Block> _queue =
            Channel.CreateUnbounded<Block>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        private readonly IAudioEncoder _encoder;
        private readonly Task _writer;

        public Track(RecordingTarget target, Action<Exception> onFault)
        {
            _encoder = target.Encoder;
            _writer = Task.Run(() => WriteLoopAsync(onFault));
        }

        public bool TryWrite(ReadOnlySpan<float> samples)
        {
            var buffer = ArrayPool<float>.Shared.Rent(samples.Length);
            samples.CopyTo(buffer);
            if (_queue.Writer.TryWrite(new Block(buffer, samples.Length)))
            {
                return true;
            }

            ArrayPool<float>.Shared.Return(buffer);
            return false;
        }

        public void Complete() => _queue.Writer.TryComplete();

        public async Task CloseAsync()
        {
            await _writer.ConfigureAwait(false);
            _encoder.Dispose();
        }

        private async Task WriteLoopAsync(Action<Exception> onFault)
        {
            bool failed = false;
            await foreach (var block in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (!failed) _encoder.Write(block.Samples, 0, block.Length);
                }
                catch (Exception ex)
                {
                    failed = true;
                    onFault(ex);
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(block.Samples);
                }
            }
        }
    }

    private readonly record struct Block(float[] Samples, int Length);
}
