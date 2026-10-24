namespace PodcastRecorder.Audio.Mixing;

/// <summary>
/// Receives each block of the program mix on the audio thread. Implementations must return
/// quickly: copy the block and do slow work (encoding, disk, network) elsewhere.
/// </summary>
public interface IProgramSink
{
    void Write(ReadOnlySpan<float> samples);
}
