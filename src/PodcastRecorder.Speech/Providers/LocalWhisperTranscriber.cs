using PodcastRecorder.Speech.Audio;
using Whisper.net;
using Whisper.net.Ggml;

namespace PodcastRecorder.Speech.Providers;

/// <summary>
/// OpenAI Whisper running on this computer through whisper.cpp: free and offline, but slower
/// than the online services on a CPU. The model is downloaded once from Hugging Face.
/// </summary>
public sealed class LocalWhisperTranscriber(string modelFolder, string modelSize) : ISpeechToText, IDisposable
{
    public static IReadOnlyList<WhisperModelOption> Models { get; } =
    [
        new("tiny", "Whisper_Tiny", GgmlType.Tiny, 75),
        new("base", "Whisper_Base", GgmlType.Base, 142),
        new("small", "Whisper_Small", GgmlType.Small, 466),
        new("medium", "Whisper_Medium", GgmlType.Medium, 1530),
        new("large-v3-turbo", "Whisper_LargeV3Turbo", GgmlType.LargeV3Turbo, 1620),
    ];

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    private WhisperModelOption Model =>
        Models.FirstOrDefault(m => m.Id == modelSize) ?? throw new SpeechException($"Bilinmeyen Whisper modeli: {modelSize}");

    public string ModelPath => Path.Combine(modelFolder, $"ggml-{Model.Id}.bin");

    public async Task PrepareAsync(IProgress<TranscriptionProgress>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(ModelPath))
        {
            await DownloadModelAsync(progress, cancellationToken);
        }

        progress?.Report(new TranscriptionProgress(Texts.Get("Stage_LoadingModel"), 0));
        _factory ??= WhisperFactory.FromPath(ModelPath);
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeChunkAsync(AudioChunk chunk, string? language, CancellationToken cancellationToken)
    {
        if (_factory is null)
        {
            throw new InvalidOperationException("PrepareAsync must run first.");
        }

        _processor ??= string.IsNullOrWhiteSpace(language)
            ? _factory.CreateBuilder().WithLanguageDetection().Build()
            : _factory.CreateBuilder().WithLanguage(language).Build();

        var segments = new List<TranscriptSegment>();
        using var wav = new MemoryStream(chunk.Wav, writable: false);
        await foreach (var segment in _processor.ProcessAsync(wav, cancellationToken))
        {
            segments.Add(new TranscriptSegment(segment.Start, segment.End, segment.Text));
        }

        return segments;
    }

    private async Task DownloadModelAsync(IProgress<TranscriptionProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(modelFolder);
        var temp = ModelPath + ".part";
        var model = Model;
        try
        {
            await using (var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(model.Type, cancellationToken: cancellationToken))
            await using (var file = File.Create(temp))
            {
                var buffer = new byte[1 << 20];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                    progress?.Report(new TranscriptionProgress(
                        Texts.Format("Stage_DownloadingModel", total / 1_000_000, model.ApproximateMegabytes),
                        Math.Min(total / (model.ApproximateMegabytes * 1_000_000.0), 1)));
                }
            }

            File.Move(temp, ModelPath, overwrite: true);
        }
        catch (HttpRequestException ex)
        {
            throw new SpeechException(Texts.Format("Whisper_DownloadFailed", ex.Message), ex);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}

/// <summary>A Whisper model size; <see cref="DisplayName"/> is in the UI language.</summary>
public sealed record WhisperModelOption(string Id, string DisplayNameKey, GgmlType Type, int ApproximateMegabytes)
{
    public string DisplayName => Texts.Get(DisplayNameKey);
}
