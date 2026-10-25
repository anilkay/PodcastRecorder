using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NAudio.Wave;
using PodcastRecorder.Audio.Devices;
using static PodcastRecorder.Audio.PortAudio.PortAudioNative;

namespace PodcastRecorder.Audio.PortAudio;

/// <summary>
/// Plays <see cref="AudioFormat.Mix"/> on a PortAudio device. PortAudio's callback thread pulls
/// each block from the source, the same way WASAPI's event thread does on Windows.
/// </summary>
internal sealed unsafe class PortAudioOutput : IAudioOutput
{
    private const double MinimumLatency = 0.05;

    private readonly ISampleProvider _source;
    private GCHandle _self;
    private IntPtr _stream;
    private float[] _buffer = new float[4096];
    private volatile bool _running;

    public PortAudioOutput(int device, ISampleProvider source)
    {
        if (!AudioFormat.IsMixFormat(source.WaveFormat)) throw new ArgumentException("Source must be in the mix format.", nameof(source));
        _source = source;
        var info = Pa_GetDeviceInfo(device);
        var parameters = new StreamParameters
        {
            Device = device,
            ChannelCount = AudioFormat.Channels,
            SampleFormat = new CULong((nuint)Float32),
            SuggestedLatency = Math.Max(MinimumLatency, info->DefaultLowOutputLatency),
        };
        if (Pa_IsFormatSupported(null, &parameters, AudioFormat.SampleRate) < 0)
        {
            throw new InvalidOperationException(
                Texts.Format("Pa_OutputFormat", Text(info->Name)));
        }

        _self = GCHandle.Alloc(this);
        int error = Pa_OpenStream(out _stream, null, &parameters, AudioFormat.SampleRate, new CULong(0), new CULong(0),
            &Callback, (void*)GCHandle.ToIntPtr(_self));
        if (error < 0)
        {
            _self.Free();
            Check(error, Texts.Get("Pa_OutputOpenFailed"));
        }
    }

    public bool IsRunning => _running;

    public event EventHandler<Exception?>? Stopped;

    public void Start()
    {
        Check(Pa_StartStream(_stream), Texts.Get("Pa_OutputStartFailed"));
        _running = true;
    }

    public void Stop()
    {
        if (!_running || _stream == IntPtr.Zero) return;
        _running = false;
        Pa_StopStream(_stream);
        Stopped?.Invoke(this, null);
    }

    public void Dispose()
    {
        Stop();
        if (_stream != IntPtr.Zero)
        {
            Pa_CloseStream(_stream);
            _stream = IntPtr.Zero;
        }

        if (_self.IsAllocated) _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Callback(void* input, void* output, CULong frameCount, void* timeInfo, CULong statusFlags, void* userData)
    {
        var self = (PortAudioOutput)GCHandle.FromIntPtr((IntPtr)userData).Target!;
        int samples = (int)frameCount.Value * AudioFormat.Channels;
        var destination = new Span<float>(output, samples);
        try
        {
            if (self._buffer.Length < samples) self._buffer = new float[samples];
            int read = self._source.Read(self._buffer, 0, samples);
            self._buffer.AsSpan(0, read).CopyTo(destination);
            destination[read..].Clear();
            return Continue;
        }
        catch (Exception ex)
        {
            // Never let an exception cross into native code; stop and report it instead.
            destination.Clear();
            self._running = false;
            ThreadPool.QueueUserWorkItem(_ => self.Stopped?.Invoke(self, ex));
            return Abort;
        }
    }
}
