using NAudio.Wave;

namespace PodcastRecorder.Audio.Devices;

/// <summary>
/// The platform's sound system: lists devices and opens them. WASAPI on Windows; the
/// cross-platform app uses PortAudio on Linux and macOS.
/// </summary>
public interface IAudioBackend : IDisposable
{
    IReadOnlyList<AudioDevice> Inputs { get; }

    IReadOnlyList<AudioDevice> Outputs { get; }

    string? DefaultInputId { get; }

    string? DefaultOutputId { get; }

    /// <summary>Opens an output device that pulls its audio from <paramref name="source"/> once started.</summary>
    IAudioOutput OpenOutput(string deviceId, ISampleProvider source);

    /// <summary>Opens a capture device; it delivers audio in <see cref="AudioFormat.Mix"/> once started.</summary>
    IAudioInput OpenInput(string deviceId);
}

/// <summary>A playing output device. Its clock drives the whole engine.</summary>
public interface IAudioOutput : IDisposable
{
    bool IsRunning { get; }

    /// <summary>Raised when the device stops, with the error if it failed.</summary>
    event EventHandler<Exception?>? Stopped;

    void Start();

    void Stop();
}

/// <summary>A capture device in the mix format.</summary>
public interface IAudioInput : ISampleProvider, IDisposable
{
    string DeviceId { get; }

    string DeviceName { get; }

    /// <summary>Raised when capture stops, with the error if the device failed.</summary>
    event EventHandler<Exception?>? Stopped;

    void Start();
}
