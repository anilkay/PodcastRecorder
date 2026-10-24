using NAudio.Wave;

namespace PodcastRecorder.Audio.Sources;

/// <summary>Opens an audio file as a seekable stream.</summary>
public delegate WaveStream AudioFileOpener(string path);

public static class AudioFiles
{
    public static IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac" };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// WAV is read directly; everything else goes through Windows Media Foundation.
    /// </summary>
    public static WaveStream Open(string path) =>
        string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)
            ? new WaveFileReader(path)
            : new MediaFoundationReader(path);
}
