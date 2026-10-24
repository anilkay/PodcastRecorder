using System.Net.Http.Headers;
using System.Text.Json;
using PodcastRecorder.Speech.Audio;

namespace PodcastRecorder.Speech.Providers;

/// <summary>Azure AI Speech "fast transcription": synchronous, timed phrases.</summary>
public sealed class AzureSpeechTranscriber(HttpClient http, AzureSpeechEndpoint endpoint) : ISpeechToText
{
    /// <summary>Without a language, Azure picks among these.</summary>
    private static readonly string[] DetectionLocales = ["tr-TR", "en-US", "de-DE"];

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeChunkAsync(AudioChunk chunk, string? language, CancellationToken cancellationToken)
    {
        using var request = endpoint.CreateRequest(endpoint.TranscriptionUrl);
        var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(chunk.Wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "audio", "audio.wav");
        var locales = string.IsNullOrWhiteSpace(language) ? DetectionLocales : [AzureSpeechEndpoint.Locale(language)];
        form.Add(new StringContent(JsonSerializer.Serialize(new { locales })), "definition");
        request.Content = form;

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await SpeechException.FromResponseAsync(response, cancellationToken);
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return Parse(json.RootElement);
    }

    internal static IReadOnlyList<TranscriptSegment> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("phrases", out var phrases) || phrases.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return phrases.EnumerateArray()
            .Select(p =>
            {
                var start = TimeSpan.FromMilliseconds(p.GetProperty("offsetMilliseconds").GetDouble());
                return new TranscriptSegment(
                    start,
                    start + TimeSpan.FromMilliseconds(p.GetProperty("durationMilliseconds").GetDouble()),
                    p.GetProperty("text").GetString() ?? "");
            })
            .ToList();
    }
}
