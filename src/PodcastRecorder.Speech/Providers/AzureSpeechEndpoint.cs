namespace PodcastRecorder.Speech.Providers;

/// <summary>An Azure AI Speech resource (also created from Azure AI Foundry): its region and key.</summary>
public sealed record AzureSpeechEndpoint(string Region, string Key)
{
    public Uri TranscriptionUrl =>
        new($"https://{CleanRegion}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15");

    public Uri SynthesisUrl => new($"https://{CleanRegion}.tts.speech.microsoft.com/cognitiveservices/v1");

    private string CleanRegion => Region.Trim().Replace(" ", "").ToLowerInvariant();

    internal HttpRequestMessage CreateRequest(Uri url)
    {
        if (string.IsNullOrWhiteSpace(Region))
        {
            throw new SpeechException(Texts.Get("Azure_NoRegion"));
        }

        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new SpeechException(Texts.Get("Azure_NoKey"));
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", Key.Trim());
        return request;
    }

    /// <summary>Azure wants locales such as tr-TR; a bare language code gets its most common region.</summary>
    internal static string Locale(string language) => language.Contains('-') ? language : language.ToLowerInvariant() switch
    {
        "tr" => "tr-TR",
        "en" => "en-US",
        "de" => "de-DE",
        "fr" => "fr-FR",
        "es" => "es-ES",
        "it" => "it-IT",
        "ar" => "ar-SA",
        "ru" => "ru-RU",
        "nl" => "nl-NL",
        "pt" => "pt-BR",
        "ja" => "ja-JP",
        "zh" => "zh-CN",
        var other => $"{other}-{other.ToUpperInvariant()}",
    };
}
