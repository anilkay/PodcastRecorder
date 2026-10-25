using NAudio.Lame;
using NAudio.Wave;
using PodcastRecorder.Audio.Recording.Lame;

namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// Picks the MP3 encoder for the platform: the LAME DLL bundled with NAudio.Lame on Windows,
/// the system's libmp3lame elsewhere. Both take 16-bit PCM bytes.
/// </summary>
public static class Mp3Writers
{
    public static Stream CreateFile(string path, WaveFormat pcmFormat, int bitrateKbps) =>
        OperatingSystem.IsWindows()
            ? new LameMP3FileWriter(path, pcmFormat, bitrateKbps)
            : new LameMp3Stream(File.Create(path), pcmFormat, bitrateKbps, ownsOutput: true);

    /// <summary>Encodes into <paramref name="output"/>, which stays open when the encoder is disposed.</summary>
    public static Stream Create(Stream output, WaveFormat pcmFormat, int bitrateKbps) =>
        OperatingSystem.IsWindows()
            ? new LameMP3FileWriter(output, pcmFormat, bitrateKbps)
            : new LameMp3Stream(output, pcmFormat, bitrateKbps, ownsOutput: false);
}
