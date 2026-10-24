using PodcastRecorder.Speech.Audio;

namespace PodcastRecorder.Speech;

public sealed record TranscriptionProgress(string Stage, double Fraction);

/// <summary>A speech recognition service. Recordings are sent in pieces of <see cref="ChunkLength"/>.</summary>
public interface ISpeechToText
{
    /// <summary>Upload limits decide this for online services; 10 minutes of 16 kHz mono WAV is about 19 MB.</summary>
    TimeSpan ChunkLength => TimeSpan.FromMinutes(10);

    /// <summary>Downloads models or checks settings before the first piece.</summary>
    Task PrepareAsync(IProgress<TranscriptionProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <param name="language">ISO 639-1 code such as "tr", or null to detect it.</param>
    /// <returns>Segments timed from the start of the chunk.</returns>
    Task<IReadOnlyList<TranscriptSegment>> TranscribeChunkAsync(AudioChunk chunk, string? language, CancellationToken cancellationToken);
}
