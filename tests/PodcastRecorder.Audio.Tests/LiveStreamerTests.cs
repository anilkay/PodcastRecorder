using System.Net;
using System.Net.Sockets;
using System.Text;
using PodcastRecorder.Audio.Streaming;

namespace PodcastRecorder.Audio.Tests;

public class LiveStreamerTests
{
    [Fact]
    public async Task Logs_in_with_put_and_streams_mp3()
    {
        using var server = new FakeIcecast("HTTP/1.1 100 Continue");
        var streamer = new LiveStreamer();

        await streamer.StartAsync(server.Settings("/canli"));
        Assert.Equal(LiveState.Live, streamer.State);
        for (int i = 0; i < 50; i++)
        {
            streamer.Write(Enumerable.Repeat(0.1f, 1920).ToArray());
        }

        await streamer.StopAsync();
        var (head, body) = await server.ReceivedAsync();

        Assert.Equal(LiveState.Offline, streamer.State);
        Assert.StartsWith("PUT /canli HTTP/1.1\r\n", head);
        Assert.Contains("Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("source:gizli")), head);
        Assert.Contains("Content-Type: audio/mpeg", head);
        Assert.Contains("Ice-Name: Deneme yayını", head);
        Assert.True(body.Length > 1000, $"only {body.Length} bytes of audio arrived");
        Assert.Equal(0xFF, body[0]); // MP3 frame sync
        Assert.Equal(0xE0, body[1] & 0xE0);
        Assert.Equal(TimeSpan.FromSeconds(1), streamer.Elapsed);
    }

    [Fact]
    public async Task Wrong_password_is_reported_and_leaves_the_stream_offline()
    {
        using var server = new FakeIcecast("HTTP/1.1 401 Authentication Required");
        var streamer = new LiveStreamer();

        var error = await Assert.ThrowsAsync<IcecastException>(() => streamer.StartAsync(server.Settings("/canli")));

        Assert.Contains("şifre", error.Message);
        Assert.Equal(LiveState.Offline, streamer.State);
    }

    [Fact]
    public async Task Closed_port_is_reported()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var streamer = new LiveStreamer();

        await Assert.ThrowsAsync<IcecastException>(() =>
            streamer.StartAsync(new IcecastSettings("127.0.0.1", port, "/canli", "source", "gizli", 128, null)));
        Assert.Equal(LiveState.Offline, streamer.State);
    }

    [Fact]
    public async Task Writes_while_offline_are_ignored()
    {
        var streamer = new LiveStreamer();
        streamer.Write(new float[960]);
        await streamer.StopAsync();

        Assert.Equal(TimeSpan.Zero, streamer.Elapsed);
    }

    [Theory]
    [InlineData("", 8000, "/canli", "Sunucu")]
    [InlineData("http://yayin", 8000, "/canli", "http://")]
    [InlineData("yayin", 0, "/canli", "Port")]
    [InlineData("yayin", 8000, "canli", "Mount")]
    [InlineData("yayin", 8000, "/can li", "Mount")]
    public void Invalid_settings_are_explained(string host, int port, string mount, string expected)
    {
        var problem = new IcecastSettings(host, port, mount, "source", "gizli", 128, null).Validate();

        Assert.NotNull(problem);
        Assert.Contains(expected, problem);
    }

    [Fact]
    public void Settings_text_does_not_contain_the_password()
    {
        var settings = new IcecastSettings("yayin", 8000, "/canli", "source", "cok-gizli", 128, null);

        Assert.DoesNotContain("cok-gizli", settings.ToString());
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK", 200)]
    [InlineData("HTTP/1.0 100 Continue", 100)]
    [InlineData("ICY 401 Unauthorized", 401)]
    [InlineData("garbage", 0)]
    public void Status_line_is_parsed(string line, int status)
    {
        Assert.Equal(status, IcecastProtocol.ParseStatus(line));
    }

    /// <summary>Accepts one source connection, answers with a fixed status and keeps what it receives.</summary>
    private sealed class FakeIcecast : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task<(string Head, byte[] Body)> _received;

        public FakeIcecast(string statusLine)
        {
            _listener.Start();
            _received = Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var head = new StringBuilder();
                var one = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)
                       && await stream.ReadAsync(one) == 1)
                {
                    head.Append((char)one[0]);
                }

                await stream.WriteAsync(Encoding.ASCII.GetBytes(statusLine + "\r\n\r\n"));
                using var body = new MemoryStream();
                await stream.CopyToAsync(body);
                return (Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(head.ToString())), body.ToArray());
            });
        }

        public IcecastSettings Settings(string mount) =>
            new("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port, mount, "source", "gizli", 128, "Deneme yayını");

        public Task<(string Head, byte[] Body)> ReceivedAsync() => _received.WaitAsync(TimeSpan.FromSeconds(10));

        public void Dispose() => _listener.Stop();
    }
}
