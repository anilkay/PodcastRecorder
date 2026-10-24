using System.Net.Http.Headers;

namespace PodcastRecorder.Speech.Providers;

/// <summary>
/// Where an OpenAI-style audio API lives: OpenAI itself, or an Azure OpenAI / Azure AI Foundry
/// resource through its v1 API, where the deployment name takes the place of the model name.
/// </summary>
public sealed record OpenAiEndpoint(Uri BaseUrl, string ApiKey, bool IsAzure)
{
    public const string DefaultOpenAiUrl = "https://api.openai.com/v1/";

    public static OpenAiEndpoint OpenAi(string apiKey, string? baseUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(baseUrl) ? DefaultOpenAiUrl : baseUrl.Trim();
        return new OpenAiEndpoint(new Uri(url.EndsWith('/') ? url : url + "/"), apiKey, IsAzure: false);
    }

    /// <summary>
    /// Accepts any address from the Azure or Foundry portal, such as
    /// <c>https://name.openai.azure.com/</c> or <c>https://name.services.ai.azure.com/api/projects/x</c>;
    /// only the host is used.
    /// </summary>
    public static OpenAiEndpoint Azure(string endpoint, string apiKey)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new SpeechException("Azure adresi https:// ile başlayan tam bir adres olmalı, ör. https://kaynak-adi.openai.azure.com");
        }

        return new OpenAiEndpoint(new Uri($"https://{uri.Authority}/openai/v1/"), apiKey, IsAzure: true);
    }

    internal HttpRequestMessage CreateRequest(string path)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new SpeechException("API anahtarı girilmemiş.");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUrl, path));
        if (IsAzure)
        {
            request.Headers.Add("api-key", ApiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        return request;
    }
}
