using System.Net.Http.Headers;
using System.Text.Json;
using PodcastRecorder.Speech.Audio;

namespace PodcastRecorder.Speech.Providers;

/// <summary>
/// Speech to text through <c>/audio/transcriptions</c>: whisper-1, gpt-4o-transcribe and
/// gpt-4o-mini-transcribe on OpenAI, or their deployments on Azure.
/// Only Whisper returns timed segments; the others give one block of text per chunk.
/// </summary>
public sealed class OpenAiTranscriber(HttpClient http, OpenAiEndpoint endpoint, string model) : ISpeechToText
{
    public static IReadOnlyList<string> SuggestedModels { get; } = ["whisper-1", "gpt-4o-transcribe", "gpt-4o-mini-transcribe"];

    private bool ReturnsSegments => model.Contains("whisper", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeChunkAsync(AudioChunk chunk, string? language, CancellationToken cancellationToken)
    {
        using var request = endpoint.CreateRequest("audio/transcriptions");
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(chunk.Wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        form.Add(new StringContent(model), "model");
        if (!string.IsNullOrWhiteSpace(language))
        {
            form.Add(new StringContent(language), "language");
        }

        if (ReturnsSegments)
        {
            form.Add(new StringContent("verbose_json"), "response_format");
            form.Add(new StringContent("segment"), "timestamp_granularities[]");
        }
        else
        {
            form.Add(new StringContent("json"), "response_format");
        }

        request.Content = form;
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await SpeechException.FromResponseAsync(response, cancellationToken);
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return Parse(json.RootElement, chunk.Duration);
    }

    internal static IReadOnlyList<TranscriptSegment> Parse(JsonElement root, TimeSpan chunkDuration)
    {
        if (root.TryGetProperty("segments", out var segments) && segments.ValueKind == JsonValueKind.Array)
        {
            return segments.EnumerateArray()
                .Select(s => new TranscriptSegment(
                    TimeSpan.FromSeconds(s.GetProperty("start").GetDouble()),
                    TimeSpan.FromSeconds(s.GetProperty("end").GetDouble()),
                    s.GetProperty("text").GetString() ?? ""))
                .ToList();
        }

        var text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
        return string.IsNullOrWhiteSpace(text) ? [] : [new TranscriptSegment(TimeSpan.Zero, chunkDuration, text)];
    }
}
