using NAudio.Wave;

namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// Writes the program bus to a file. Add a new output format by implementing this interface
/// and registering it in <see cref="EncoderCatalog"/>.
/// </summary>
public interface IAudioEncoder : IDisposable
{
    /// <summary>File extension including the dot, e.g. ".mp3".</summary>
    string FileExtension { get; }

    void Open(string path, WaveFormat inputFormat);

    /// <summary>Writes interleaved float samples in the format passed to <see cref="Open"/>.</summary>
    void Write(float[] samples, int offset, int count);
}
