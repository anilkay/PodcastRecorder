using System.Net.Http.Headers;
using System.Security;

namespace PodcastRecorder.Speech.Providers;

/// <summary>Azure AI Speech neural voices through the REST API.</summary>
public sealed class AzureSpeechSynthesizer(HttpClient http, AzureSpeechEndpoint endpoint) : ITextToSpeech
{
    public static IReadOnlyList<string> SuggestedVoices { get; } =
    [
        "tr-TR-EmelNeural",
        "tr-TR-AhmetNeural",
        "en-US-AvaMultilingualNeural",
        "en-US-AndrewMultilingualNeural",
        "de-DE-SeraphinaMultilingualNeural",
    ];

    public string FileExtension => ".mp3";

    public int MaxCharacters => 5000;

    public async Task SynthesizeAsync(string text, string voice, string outputPath, CancellationToken cancellationToken)
    {
        using var request = endpoint.CreateRequest(endpoint.SynthesisUrl);
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-48khz-192kbitrate-mono-mp3");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PodcastRecorder", "1.0"));
        request.Content = new StringContent(Ssml(text, voice));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ssml+xml");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await SpeechException.FromResponseAsync(response, cancellationToken);
        }

        await SpeechFiles.WriteAsync(response, outputPath, cancellationToken);
    }

    internal static string Ssml(string text, string voice)
    {
        var name = string.IsNullOrWhiteSpace(voice) ? SuggestedVoices[0] : voice.Trim();
        var parts = name.Split('-');
        var locale = parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : "tr-TR";
        return $"<speak version='1.0' xml:lang='{locale}'><voice name='{SecurityElement.Escape(name)}'>{SecurityElement.Escape(text)}</voice></speak>";
    }
}
