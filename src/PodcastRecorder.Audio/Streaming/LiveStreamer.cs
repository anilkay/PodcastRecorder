using System.Buffers;
using System.Net.Sockets;
using System.Threading.Channels;
using NAudio.Lame;
using NAudio.Wave;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Streaming;

public enum LiveState
{
    Offline,
    Connecting,
    Live,
}

/// <summary>
/// Sends the program bus to an Icecast server as MP3. Like <see cref="Recorder"/> it copies
/// blocks on the audio thread and encodes and sends them on a background task. The queue is
/// bounded: if the network falls behind, audio is dropped instead of piling up in memory.
/// </summary>
public sealed class LiveStreamer : IProgramSink
{
    /// <summary>About five seconds of audio at the usual 10-30 ms output blocks.</summary>
    private const int QueueCapacity = 256;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private Channel<Block>? _queue;
    private Task? _sender;
    private IcecastSettings? _settings;
    private long _frames;
    private long _droppedBlocks;
    private volatile LiveState _state = LiveState.Offline;

    public LiveState State => _state;

    /// <summary>Length of audio sent since the stream started.</summary>
    public TimeSpan Elapsed => AudioFormat.FramesToTime(Interlocked.Read(ref _frames));

    /// <summary>Blocks dropped because the connection could not keep up.</summary>
    public long DroppedBlocks => Interlocked.Read(ref _droppedBlocks);

    /// <summary>Raised on a background thread when the connection breaks; the stream is then offline.</summary>
    public event EventHandler<Exception>? Faulted;

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
                throw new InvalidOperationException("Yayın zaten açık.");
            }

            _state = LiveState.Connecting;
        }

        var client = new TcpClient { NoDelay = true };
        LameMP3FileWriter? encoder = null;
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
                throw new IcecastException($"Sunucu {ConnectTimeout.TotalSeconds:0} saniye içinde yanıt vermedi.");
            }
            catch (SocketException ex)
            {
                throw new IcecastException($"Sunucuya bağlanılamadı: {ex.Message}");
            }

            var stream = client.GetStream();
            stream.WriteTimeout = (int)ConnectTimeout.TotalMilliseconds;
            encoder = new LameMP3FileWriter(stream, new WaveFormat(AudioFormat.SampleRate, 16, AudioFormat.Channels), settings.BitrateKbps);
            var queue = Channel.CreateBounded<Block>(new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

            lock (_gate)
            {
                _settings = settings;
                _queue = queue;
                Interlocked.Exchange(ref _frames, 0);
                Interlocked.Exchange(ref _droppedBlocks, 0);
                var ownedEncoder = encoder;
                _sender = Task.Run(() => SendLoopAsync(queue, ownedEncoder, client));
                _state = LiveState.Live;
            }
        }
        catch
        {
            encoder?.Dispose();
            client.Dispose();
            _state = LiveState.Offline;
            throw;
        }
    }

    /// <summary>Stops sending and closes the connection.</summary>
    public async Task StopAsync()
    {
        Task? sender;
        lock (_gate)
        {
            if (_state != LiveState.Live)
            {
                return;
            }

            _state = LiveState.Offline;
            _queue?.Writer.TryComplete();
            _queue = null;
            sender = _sender;
            _sender = null;
            _settings = null;
        }

        if (sender is not null) await sender.ConfigureAwait(false);
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
    private async Task SendLoopAsync(Channel<Block> queue, LameMP3FileWriter encoder, TcpClient client)
    {
        Exception? failure = null;
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
                        _state = LiveState.Offline;
                        _queue = null;
                        _sender = null;
                        _settings = null;
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
        if (failure is not null)
        {
            Faulted?.Invoke(this, new IcecastException($"Yayın bağlantısı koptu: {failure.Message}"));
        }
    }

    private readonly record struct Block(float[] Samples, int Length);
}
