using System.Buffers;
using System.Net.Sockets;
using System.Threading.Channels;
using NAudio.Wave;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Streaming;

public enum LiveState
{
    Offline,
    Connecting,
    Live,

    /// <summary>The connection broke; trying to connect again.</summary>
    Reconnecting,
}

/// <summary>
/// Sends the program bus to an Icecast server as MP3. Like <see cref="Recorder"/> it copies
/// blocks on the audio thread and encodes and sends them on a background task. The queue is
/// bounded: if the network falls behind, audio is dropped instead of piling up in memory.
/// If the connection breaks, it reconnects on its own a few times before giving up.
/// </summary>
public sealed class LiveStreamer : IProgramSink
{
    /// <summary>About five seconds of audio at the usual 10-30 ms output blocks.</summary>
    private const int QueueCapacity = 256;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly Func<Stream, IcecastSettings, Stream> _createEncoder;
    private readonly Lock _gate = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private Channel<Block>? _queue;
    private Task? _sender;
    private Task? _reconnect;
    private CancellationTokenSource? _session;
    private IcecastSettings? _settings;
    private long _frames;
    private long _droppedBlocks;
    private volatile LiveState _state = LiveState.Offline;

    public LiveStreamer()
        : this((output, settings) => Mp3Writers.Create(
            output, new WaveFormat(AudioFormat.SampleRate, 16, AudioFormat.Channels), settings.BitrateKbps))
    {
    }

    /// <param name="createEncoder">Wraps the connection in a stream that takes 16-bit PCM.</param>
    internal LiveStreamer(Func<Stream, IcecastSettings, Stream> createEncoder)
    {
        _createEncoder = createEncoder;
    }

    public LiveState State => _state;

    public int MaxReconnectAttempts { get; init; } = 10;

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Length of audio sent since the stream started.</summary>
    public TimeSpan Elapsed => AudioFormat.FramesToTime(Interlocked.Read(ref _frames));

    /// <summary>Blocks dropped because the connection could not keep up.</summary>
    public long DroppedBlocks => Interlocked.Read(ref _droppedBlocks);

    /// <summary>Raised on a background thread when reconnecting failed for good; the stream is then offline.</summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>Raised on a background thread when the connection breaks and when it comes back.</summary>
    public event EventHandler<LiveState>? StateChanged;

    /// <summary>Connects, logs in and starts sending. Throws with a user-readable message on failure.</summary>
    public async Task StartAsync(IcecastSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.Validate() is { } problem)
        {
            throw new ArgumentException(problem, nameof(settings));
        }

        lock (_gate)
        {
            if (_state != LiveState.Offline)
            {
                throw new InvalidOperationException(Texts.Get("Live_AlreadyLive"));
            }

            _state = LiveState.Connecting;
        }

        var session = new CancellationTokenSource();
        try
        {
            var (client, encoder) = await ConnectAsync(settings, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _settings = settings;
                _session = session;
                Interlocked.Exchange(ref _frames, 0);
                Interlocked.Exchange(ref _droppedBlocks, 0);
                BeginSending(settings, client, encoder, session.Token);
            }
        }
        catch
        {
            session.Dispose();
            _state = LiveState.Offline;
            throw;
        }
    }

    /// <summary>Opens the connection, logs in and wraps it in an MP3 encoder.</summary>
    private async Task<(TcpClient Client, Stream Encoder)> ConnectAsync(IcecastSettings settings, CancellationToken cancellationToken)
    {
        // The send timeout is set before connecting: macOS refuses socket options (EINVAL) on a
        // connection the server has already reset, which would hide the reset from the reconnect logic.
        var client = new TcpClient { NoDelay = true, SendTimeout = (int)ConnectTimeout.TotalMilliseconds };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            try
            {
                await client.ConnectAsync(settings.Host, settings.Port, timeout.Token).ConfigureAwait(false);
                await IcecastProtocol.HandshakeAsync(client.GetStream(), settings, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IcecastException(Texts.Format("Live_Timeout", ConnectTimeout.TotalSeconds));
            }
            catch (SocketException ex)
            {
                throw new IcecastException(Texts.Format("Live_ConnectFailed", ex.Message));
            }

            return (client, _createEncoder(client.GetStream(), settings));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Starts the sender for a fresh connection. Call under <see cref="_gate"/>.</summary>
    private void BeginSending(IcecastSettings settings, TcpClient client, Stream encoder, CancellationToken session)
    {
        var queue = Channel.CreateBounded<Block>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _queue = queue;
        _sender = Task.Run(() => SendLoopAsync(queue, encoder, client, settings, session));
        _state = LiveState.Live;
    }

    /// <summary>Stops sending and closes the connection.</summary>
    public async Task StopAsync()
    {
        Task? sender;
        Task? reconnect;
        CancellationTokenSource? session;
        lock (_gate)
        {
            if (_state is not (LiveState.Live or LiveState.Reconnecting))
            {
                return;
            }

            _state = LiveState.Offline;
            _queue?.Writer.TryComplete();
            _queue = null;
            sender = _sender;
            _sender = null;
            reconnect = _reconnect;
            _reconnect = null;
            session = _session;
            _session = null;
            _settings = null;
        }

        session?.Cancel();
        if (sender is not null) await sender.ConfigureAwait(false);
        if (reconnect is not null) await reconnect.ConfigureAwait(false);
        session?.Dispose();
    }

    /// <summary>Sets the "now playing" title on the server. Does nothing when offline.</summary>
    public Task UpdateTitleAsync(string title)
    {
        var settings = _settings;
        return _state == LiveState.Live && settings is not null
            ? IcecastProtocol.UpdateTitleAsync(_http, settings, title, CancellationToken.None)
            : Task.CompletedTask;
    }

    /// <summary>Called by the output bus with each block of the program mix.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        var queue = _queue;
        if (_state != LiveState.Live || queue is null || samples.IsEmpty)
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
            Interlocked.Increment(ref _droppedBlocks);
        }
    }

    /// <summary>Owns the encoder and the connection and closes both when it ends.</summary>
    private async Task SendLoopAsync(Channel<Block> queue, Stream encoder, TcpClient client,
        IcecastSettings settings, CancellationToken session)
    {
        Exception? failure = null;
        bool reconnect = false; // false when the break came from StopAsync closing things
        byte[] pcm = [];
        await foreach (var block in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (failure is null)
                {
                    int bytes = PcmConversion.ToPcm16(block.Samples.AsSpan(0, block.Length), ref pcm);
                    encoder.Write(pcm, 0, bytes);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                failure = ex;
                lock (_gate)
                {
                    if (_queue == queue)
                    {
                        _state = LiveState.Reconnecting;
                        _queue = null;
                        _sender = null;
                        reconnect = true;
                    }
                }

                queue.Writer.TryComplete();
            }
            finally
            {
                ArrayPool<float>.Shared.Return(block.Samples);
            }
        }

        try
        {
            encoder.Dispose();
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The connection is already gone; the final MP3 frame does not matter.
        }

        client.Dispose();
        if (reconnect && failure is not null)
        {
            StateChanged?.Invoke(this, LiveState.Reconnecting);
            lock (_gate)
            {
                if (_state == LiveState.Reconnecting)
                {
                    _reconnect = Task.Run(() => ReconnectAsync(settings, failure, session));
                }
            }
        }
    }

    private async Task ReconnectAsync(IcecastSettings settings, Exception cause, CancellationToken session)
    {
        var last = cause;
        for (int attempt = 0; attempt < MaxReconnectAttempts; attempt++)
        {
            try
            {
                await Task.Delay(ReconnectDelay, session).ConfigureAwait(false);
                var (client, encoder) = await ConnectAsync(settings, session).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_state != LiveState.Reconnecting || session.IsCancellationRequested)
                    {
                        encoder.Dispose();
                        client.Dispose();
                        return;
                    }

                    _reconnect = null;
                    BeginSending(settings, client, encoder, session);
                }

                StateChanged?.Invoke(this, LiveState.Live);
                return;
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                last = ex;
            }
        }

        lock (_gate)
        {
            if (_state != LiveState.Reconnecting || session.IsCancellationRequested)
            {
                return;
            }

            _state = LiveState.Offline;
            _reconnect = null;
            _settings = null;
            _session = null;
        }

        Faulted?.Invoke(this, new IcecastException(
            Texts.Format("Live_ReconnectGaveUp", MaxReconnectAttempts, last.Message)));
    }

    private readonly record struct Block(float[] Samples, int Length);
}
