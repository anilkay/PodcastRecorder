using NAudio.Wave;
using PodcastRecorder.Audio.Sources;
using PodcastRecorder.Speech.Audio;

namespace PodcastRecorder.Speech;

/// <summary>Runs a whole recording through a speech recognition service, piece by piece.</summary>
public static class Transcriber
{
    /// <summary>Reads the file on the calling thread; call it from a background task.</summary>
    public static async Task<Transcript> TranscribeFileAsync(
        ISpeechToText service, string audioPath, string? language,
        IProgress<TranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var reader = AudioFiles.Open(audioPath);
        return await TranscribeAsync(service, reader.ToSampleProvider(), reader.TotalTime, language, progress, cancellationToken);
    }

    public static async Task<Transcript> TranscribeAsync(
        ISpeechToText service, ISampleProvider audio, TimeSpan totalLength, string? language,
        IProgress<TranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await service.PrepareAsync(progress, cancellationToken);
        var segments = new List<TranscriptSegment>();
        double total = Math.Max(totalLength.TotalSeconds, 1);

        foreach (var chunk in SpeechAudio.ReadChunks(audio, service.ChunkLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new TranscriptionProgress("Yazıya çevriliyor", Math.Min(chunk.Offset.TotalSeconds / total, 1)));

            var part = await service.TranscribeChunkAsync(chunk, language, cancellationToken);
            foreach (var segment in part)
            {
                var text = segment.Text.Trim();
                if (text.Length == 0) continue;

                var end = segment.End > segment.Start ? segment.End : chunk.Duration;
                segments.Add(new TranscriptSegment(chunk.Offset + segment.Start, chunk.Offset + Min(end, chunk.Duration), text));
            }
        }

        progress?.Report(new TranscriptionProgress("Bitti", 1));
        return new Transcript(segments);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
