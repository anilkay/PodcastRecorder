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
        new("tiny", "Tiny (75 MB, en hızlı)", GgmlType.Tiny, 75),
        new("base", "Base (142 MB)", GgmlType.Base, 142),
        new("small", "Small (466 MB)", GgmlType.Small, 466),
        new("medium", "Medium (1,5 GB)", GgmlType.Medium, 1530),
        new("large-v3-turbo", "Large v3 Turbo (1,6 GB, en doğru)", GgmlType.LargeV3Turbo, 1620),
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

        progress?.Report(new TranscriptionProgress("Whisper modeli yükleniyor", 0));
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
                        $"Whisper modeli indiriliyor ({total / 1_000_000} / ~{model.ApproximateMegabytes} MB)",
                        Math.Min(total / (model.ApproximateMegabytes * 1_000_000.0), 1)));
                }
            }

            File.Move(temp, ModelPath, overwrite: true);
        }
        catch (HttpRequestException ex)
        {
            throw new SpeechException($"Whisper modeli indirilemedi: {ex.Message}", ex);
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

public sealed record WhisperModelOption(string Id, string DisplayName, GgmlType Type, int ApproximateMegabytes);
