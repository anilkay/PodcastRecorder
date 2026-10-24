using System.Net.Http.Json;

namespace PodcastRecorder.Speech.Providers;

/// <summary>Text to speech through <c>/audio/speech</c> (tts-1, tts-1-hd, gpt-4o-mini-tts).</summary>
public sealed class OpenAiSpeechSynthesizer(HttpClient http, OpenAiEndpoint endpoint, string model) : ITextToSpeech
{
    public static IReadOnlyList<string> SuggestedModels { get; } = ["gpt-4o-mini-tts", "tts-1-hd", "tts-1"];

    public static IReadOnlyList<string> Voices { get; } =
        ["alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse"];

    public string FileExtension => ".mp3";

    public int MaxCharacters => 4096;

    public async Task SynthesizeAsync(string text, string voice, string outputPath, CancellationToken cancellationToken)
    {
        using var request = endpoint.CreateRequest("audio/speech");
        request.Content = JsonContent.Create(new
        {
            model,
            input = text,
            voice = string.IsNullOrWhiteSpace(voice) ? Voices[0] : voice.Trim(),
            response_format = "mp3",
        });

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await SpeechException.FromResponseAsync(response, cancellationToken);
        }

        await SpeechFiles.WriteAsync(response, outputPath, cancellationToken);
    }
}
