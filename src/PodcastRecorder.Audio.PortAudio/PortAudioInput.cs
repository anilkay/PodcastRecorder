using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NAudio.Wave;
using PodcastRecorder.Audio.Devices;
using PodcastRecorder.Audio.Sources;
using static PodcastRecorder.Audio.PortAudio.PortAudioNative;

namespace PodcastRecorder.Audio.PortAudio;

/// <summary>
/// Captures a PortAudio device and exposes it in the mix format. Like the WASAPI microphone it
/// buffers between the capture and output clocks and drops the buffer when it lags too far behind.
/// </summary>
internal sealed unsafe class PortAudioInput : IAudioInput
{
    private static readonly TimeSpan MaxLatency = TimeSpan.FromMilliseconds(250);

    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _output;
    private GCHandle _self;
    private IntPtr _stream;
    private byte[] _bytes = new byte[8192];
    private bool _running;

    public PortAudioInput(int device, string deviceId, string deviceName)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
        var info = Pa_GetDeviceInfo(device);
        var parameters = new StreamParameters
        {
            Device = device,
            ChannelCount = Math.Min(2, info->MaxInputChannels),
            SampleFormat = new CULong((nuint)Float32),
            SuggestedLatency = info->DefaultLowInputLatency,
        };

        // Prefer the mix rate so nothing needs resampling; otherwise take the device's own rate.
        double rate = Pa_IsFormatSupported(&parameters, null, AudioFormat.SampleRate) >= 0 ? AudioFormat.SampleRate : info->DefaultSampleRate;
        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat((int)rate, parameters.ChannelCount))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        _output = FormatConversion.ToMixFormat(_buffer.ToSampleProvider());

        _self = GCHandle.Alloc(this);
        int error = Pa_OpenStream(out _stream, &parameters, null, rate, new CULong(0), new CULong(0),
            &Callback, (void*)GCHandle.ToIntPtr(_self));
        if (error < 0)
        {
            _self.Free();
            Check(error, Texts.Format("Pa_MicOpenFailed", deviceName));
        }
    }

    public string DeviceId { get; }

    public string DeviceName { get; }

    public WaveFormat WaveFormat => _output.WaveFormat;

    public event EventHandler<Exception?>? Stopped;

    public void Start()
    {
        Check(Pa_StartStream(_stream), Texts.Format("Pa_MicStartFailed", DeviceName));
        _running = true;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_buffer.BufferedDuration > MaxLatency) _buffer.ClearBuffer();
        return _output.Read(buffer, offset, count);
    }

    public void Dispose()
    {
        if (_stream != IntPtr.Zero)
        {
            if (_running) Pa_StopStream(_stream);
            Pa_CloseStream(_stream);
            _stream = IntPtr.Zero;
        }

        if (_running)
        {
            _running = false;
            Stopped?.Invoke(this, null);
        }

        if (_self.IsAllocated) _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Callback(void* input, void* output, CULong frameCount, void* timeInfo, CULong statusFlags, void* userData)
    {
        var self = (PortAudioInput)GCHandle.FromIntPtr((IntPtr)userData).Target!;
        if (input is null) return Continue;
        try
        {
            int bytes = (int)frameCount.Value * self._buffer.WaveFormat.BlockAlign;
            if (self._bytes.Length < bytes) self._bytes = new byte[bytes];
            new ReadOnlySpan<byte>(input, bytes).CopyTo(self._bytes);
            self._buffer.AddSamples(self._bytes, 0, bytes);
            return Continue;
        }
        catch (Exception ex)
        {
            ThreadPool.QueueUserWorkItem(_ => self.Stopped?.Invoke(self, ex));
            return Abort;
        }
    }
}
