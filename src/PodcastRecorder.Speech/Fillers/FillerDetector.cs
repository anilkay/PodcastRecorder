using NAudio.Wave;
using PodcastRecorder.Audio.Editing;
using PodcastRecorder.Audio.Sources;
using PodcastRecorder.Speech.Audio;
using PodcastRecorder.Speech.Providers;

namespace PodcastRecorder.Speech.Fillers;

/// <summary>The fillers found, and every word heard, timed from the start of the recording.</summary>
public sealed record FillerResult(IReadOnlyList<CutRegion> Fillers, IReadOnlyList<TimedWord> Words);

/// <summary>
/// Finds filler sounds with Whisper running on this computer: the recording is transcribed word
/// by word with a prompt full of fillers, so the model writes them down instead of tidying them
/// away, and the words on the filler list become cuts. The model is downloaded on first use,
/// as for transcripts.
/// </summary>
public sealed class FillerDetector(string modelFolder, string modelSize) : IDisposable
{
    /// <summary>A Turkish lead-in that shows the model hesitations are written out.</summary>
    public const string DefaultPrompt = "Eee, ııı, şey, hmm, yani. Iıı, bugün, eee, mmm, şey konuşacağız, ee, ah.";

    /// <summary>Short pieces, for steady progress and a quick cancel.</summary>
    public static TimeSpan ChunkLength { get; } = TimeSpan.FromSeconds(60);

    private readonly LocalWhisperTranscriber _whisper = new(modelFolder, modelSize);

    /// <summary>Reads the file on the calling thread; call it from a background task.</summary>
    /// <param name="language">ISO 639-1 code; "tr" by default, null to detect.</param>
    public async Task<FillerResult> DetectFileAsync(
        string path, IReadOnlySet<string> fillers, string? language = "tr", string? prompt = DefaultPrompt,
        TimeSpan? padding = null, IProgress<TranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _whisper.PrepareAsync(progress, cancellationToken);

        using var reader = AudioFiles.Open(path);
        double total = Math.Max(reader.TotalTime.TotalSeconds, 1);
        var words = new List<TimedWord>();
        foreach (var chunk in SpeechAudio.ReadChunks(reader.ToSampleProvider(), ChunkLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new TranscriptionProgress(Texts.Get("Stage_FindingFillers"), Math.Min(chunk.Offset.TotalSeconds / total, 1)));

            foreach (var word in await _whisper.TranscribeWordsAsync(chunk, language, prompt, cancellationToken))
            {
                var end = word.End > word.Start ? Min(word.End, chunk.Duration) : word.Start;
                words.Add(word with { Start = chunk.Offset + word.Start, End = chunk.Offset + end });
            }
        }

        progress?.Report(new TranscriptionProgress(Texts.Get("Stage_Done"), 1));
        return new FillerResult(FillerMatcher.Match(words, fillers, padding), words);
    }

    public void Dispose() => _whisper.Dispose();

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
