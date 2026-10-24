using System.Net;
using System.Text.Json;

namespace PodcastRecorder.Speech;

/// <summary>A service refused a request. The message is ready to show to the user.</summary>
public sealed class SpeechException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Turns an unsuccessful HTTP response into a readable error.</summary>
    internal static async Task<SpeechException> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = ExtractMessage(body);
        var summary = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API anahtarı geçersiz ya da bu kaynağa izni yok.",
            HttpStatusCode.NotFound => "Adres, model ya da dağıtım (deployment) bulunamadı.",
            HttpStatusCode.TooManyRequests => "Hız sınırı ya da kota aşıldı; biraz sonra yeniden deneyin.",
            HttpStatusCode.RequestEntityTooLarge => "Gönderilen ses dosyası servisin sınırından büyük.",
            _ => $"Servis hata döndürdü ({(int)response.StatusCode}).",
        };
        return new SpeechException(string.IsNullOrWhiteSpace(detail) ? summary : $"{summary} {detail}");
    }

    private static string? ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString();
                if (error.TryGetProperty("message", out var message)) return message.GetString();
            }

            if (root.TryGetProperty("message", out var topMessage)) return topMessage.GetString();
        }
        catch (JsonException)
        {
        }

        return body.Length > 300 ? body[..300] : body;
    }
}
