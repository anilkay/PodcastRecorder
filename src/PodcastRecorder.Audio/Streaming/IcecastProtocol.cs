using System.Net.Http.Headers;
using System.Text;

namespace PodcastRecorder.Audio.Streaming;

/// <summary>
/// The Icecast source protocol (Icecast 2.4+): an HTTP PUT whose body is the endless MP3 stream,
/// plus the admin call that sets the "now playing" title.
/// </summary>
internal static class IcecastProtocol
{
    private const int MaxResponseBytes = 8 * 1024;

    /// <summary>Sends the request headers and waits until the server accepts the stream.</summary>
    public static async Task HandshakeAsync(Stream stream, IcecastSettings settings, CancellationToken cancellationToken)
    {
        var request = new StringBuilder()
            .Append($"PUT {Clean(settings.Mount)} HTTP/1.1\r\n")
            .Append($"Host: {Clean(settings.Host)}:{settings.Port}\r\n")
            .Append($"Authorization: Basic {Credentials(settings)}\r\n")
            .Append("User-Agent: PodcastRecorder\r\n")
            .Append("Content-Type: audio/mpeg\r\n")
            .Append("Ice-Public: 0\r\n")
            .Append($"Ice-Name: {Clean(settings.StreamName is { Length: > 0 } name ? name : "Podcast Recorder")}\r\n")
            .Append($"Ice-Audio-Info: ice-bitrate={settings.BitrateKbps};ice-channels={AudioFormat.Channels};ice-samplerate={AudioFormat.SampleRate}\r\n")
            .Append("Expect: 100-continue\r\n")
            .Append("\r\n")
            .ToString();

        await stream.WriteAsync(Encoding.UTF8.GetBytes(request), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var statusLine = await ReadStatusLineAsync(stream, cancellationToken).ConfigureAwait(false);
        int status = ParseStatus(statusLine);
        if (status is 100 or 200)
        {
            return;
        }

        throw new IcecastException(status switch
        {
            401 => "Sunucu kullanıcı adını ya da şifreyi kabul etmedi.",
            403 => "Sunucu bu mount'a yayına izin vermedi (mount kullanımda ya da kapalı olabilir).",
            404 => "Sunucu bu adreste bir Icecast yayın noktası bulamadı.",
            _ => $"Sunucu yayını reddetti: {Truncate(statusLine, 120)}",
        });
    }

    /// <summary>Sets the title listeners see. Best effort: failures are ignored.</summary>
    public static async Task UpdateTitleAsync(HttpClient http, IcecastSettings settings, string title, CancellationToken cancellationToken)
    {
        var uri = new UriBuilder("http", settings.Host, settings.Port, "/admin/metadata")
        {
            Query = $"mount={Uri.EscapeDataString(settings.Mount)}&mode=updinfo&charset=UTF-8&song={Uri.EscapeDataString(title)}",
        }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Credentials(settings));
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The title is a nicety; the stream itself keeps going.
        }
    }

    internal static int ParseStatus(string statusLine)
    {
        // "HTTP/1.1 200 OK" (Icecast) or "ICY 200 OK" (older SHOUTcast-style servers).
        var parts = statusLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[1], out int status) ? status : 0;
    }

    private static async Task<string> ReadStatusLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Read the whole response head so no header bytes are left in the socket.
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < MaxResponseBytes)
        {
            int read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IcecastException("Sunucu bağlantıyı yanıt vermeden kapattı.");
            }

            head.Add(one[0]);
            int n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n')
            {
                break;
            }
        }

        var text = Encoding.ASCII.GetString(head.ToArray());
        int end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return end >= 0 ? text[..end] : text;
    }

    private static string Credentials(IcecastSettings settings) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.User}:{settings.Password}"));

    /// <summary>Keeps user text from breaking out of its header line.</summary>
    private static string Clean(string value) => value.Replace("\r", "").Replace("\n", "");

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";
}

/// <summary>The server refused the stream; the message is meant for the user.</summary>
public sealed class IcecastException(string message) : IOException(message);
