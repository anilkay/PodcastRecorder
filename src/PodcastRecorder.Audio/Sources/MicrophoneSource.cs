using NAudio.CoreAudioApi;
using NAudio.Wave;
using PodcastRecorder.Audio.Devices;

namespace PodcastRecorder.Audio.Sources;

/// <summary>
/// Captures a WASAPI input device and exposes it in the mix format. The capture and output
/// clocks drift apart slowly, so the buffer is dropped when it lags too far behind.
/// </summary>
public sealed class MicrophoneSource : IAudioInput
{
    private static readonly TimeSpan MaxLatency = TimeSpan.FromMilliseconds(250);

    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _output;

    public MicrophoneSource(MMDevice device)
    {
        DeviceId = device.ID;
        DeviceName = device.FriendlyName;
        _capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 20);
        _buffer = new BufferedWaveProvider(FormatConversion.Simplify(_capture.WaveFormat))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        _output = FormatConversion.ToMixFormat(_buffer.ToSampleProvider());
        _capture.DataAvailable += (_, e) => _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        _capture.RecordingStopped += (_, e) => Stopped?.Invoke(this, e.Exception);
    }

    public string DeviceId { get; }

    public string DeviceName { get; }

    /// <summary>Raised when capture stops, with the error if the device failed.</summary>
    public event EventHandler<Exception?>? Stopped;

    public WaveFormat WaveFormat => _output.WaveFormat;

    public void Start() => _capture.StartRecording();

    public int Read(float[] buffer, int offset, int count)
    {
        if (_buffer.BufferedDuration > MaxLatency)
        {
            _buffer.ClearBuffer();
        }

        return _output.Read(buffer, offset, count);
    }

    public void Dispose()
    {
        _capture.StopRecording();
        _capture.Dispose();
    }
}
