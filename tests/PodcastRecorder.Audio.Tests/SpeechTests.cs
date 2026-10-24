using System.Net;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using PodcastRecorder.Speech;
using PodcastRecorder.Speech.Audio;
using PodcastRecorder.Speech.Providers;

namespace PodcastRecorder.Audio.Tests;

public class SpeechAudioTests
{
    [Fact]
    public void Chunks_cover_the_audio_and_cut_in_the_quiet_part()
    {
        // 25 s of 48 kHz stereo tone with a pause from 8.0 to 8.3 s.
        var audio = new ToneWithPause(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(0.3));

        var chunks = SpeechAudio.ReadChunks(audio, TimeSpan.FromSeconds(10)).ToList();

        Assert.Equal(3, chunks.Count);
        Assert.InRange(chunks[0].Duration.TotalSeconds, 8.0, 8.3);
        Assert.Equal(chunks[0].Duration, chunks[1].Offset);
        Assert.Equal(25, chunks.Sum(c => c.Duration.TotalSeconds), 2);

        using var reader = new WaveFileReader(new MemoryStream(chunks[0].Wav));
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(chunks[0].Duration.TotalSeconds, reader.TotalTime.TotalSeconds, 3);
    }

    [Fact]
    public async Task Transcriber_offsets_segments_and_drops_empty_ones()
    {
        var service = new EchoService();
        var audio = new ToneWithPause(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(0.3));

        var transcript = await Transcriber.TranscribeAsync(service, audio, TimeSpan.FromSeconds(25), "tr");

        Assert.Equal(3, transcript.Segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), transcript.Segments[0].Start);
        Assert.True(transcript.Segments[1].Start > TimeSpan.FromSeconds(9));
        Assert.All(transcript.Segments, s => Assert.Equal("merhaba", s.Text));
        Assert.Equal(["tr", "tr", "tr"], service.Languages);
    }

    [Fact]
    public void Srt_and_text_use_standard_timestamps()
    {
        var transcript = new Transcript(
        [
            new TranscriptSegment(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3.25), "Hoş geldiniz."),
            new TranscriptSegment(TimeSpan.FromSeconds(3661), TimeSpan.FromSeconds(3662.004), "Bir saat geçti."),
        ]);

        Assert.Equal(
            "1\n00:00:01,500 --> 00:00:03,250\nHoş geldiniz.\n\n2\n01:01:01,000 --> 01:01:02,004\nBir saat geçti.\n\n",
            transcript.ToSrt());
        Assert.StartsWith("[00:01] Hoş geldiniz.", transcript.ToPlainText());
        Assert.Contains("[1:01:01] Bir saat geçti.", transcript.ToPlainText());
    }

    private sealed class EchoService : ISpeechToText
    {
        public TimeSpan ChunkLength => TimeSpan.FromSeconds(10);

        public List<string?> Languages { get; } = [];

        public Task<IReadOnlyList<TranscriptSegment>> TranscribeChunkAsync(AudioChunk chunk, string? language, CancellationToken cancellationToken)
        {
            Languages.Add(language);
            return Task.FromResult<IReadOnlyList<TranscriptSegment>>(
            [
                new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), " merhaba "),
                new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "  "),
            ]);
        }
    }

    private sealed class ToneWithPause(TimeSpan length, TimeSpan pauseAt, TimeSpan pauseLength) : ISampleProvider
    {
        private readonly long _total = (long)(length.TotalSeconds * 48000);
        private long _frame;

        public WaveFormat WaveFormat { get; } = AudioFormat.Mix;

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = (int)Math.Min(count / 2, _total - _frame);
            for (int i = 0; i < frames; i++, _frame++)
            {
                double t = _frame / 48000.0;
                bool quiet = t >= pauseAt.TotalSeconds && t < (pauseAt + pauseLength).TotalSeconds;
                float value = quiet ? 0 : 0.5f * (float)Math.Sin(2 * Math.PI * 220 * t);
                buffer[offset + i * 2] = value;
                buffer[offset + i * 2 + 1] = value;
            }

            return frames * 2;
        }
    }
}

public class SpeechProviderTests
{
    private static readonly AudioChunk Chunk = new(TimeSpan.Zero, TimeSpan.FromSeconds(4), SpeechAudio.ToWav(new float[64000]));

    [Fact]
    public async Task OpenAi_whisper_sends_bearer_key_and_reads_segments()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"text":"a b","segments":[{"start":0.0,"end":1.5,"text":"a"},{"start":1.5,"end":3.0,"text":"b"}]}""");
        var service = new OpenAiTranscriber(new HttpClient(handler), OpenAiEndpoint.OpenAi("sk-test"), "whisper-1");

        var segments = await service.TranscribeChunkAsync(Chunk, "tr", CancellationToken.None);

        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", handler.Url);
        Assert.Equal("Bearer sk-test", handler.Headers["Authorization"]);
        Assert.Contains("verbose_json", handler.Body);
        Assert.Contains("name=language", handler.Body);
        Assert.Equal([TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3)], segments.Select(s => s.End));
    }

    [Fact]
    public async Task Azure_openai_uses_v1_path_and_api_key_header()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"text":"tek parça"}""");
        var endpoint = OpenAiEndpoint.Azure("https://kaynak.services.ai.azure.com/api/projects/podcast", "az-key");
        var service = new OpenAiTranscriber(new HttpClient(handler), endpoint, "gpt-4o-transcribe");

        var segments = await service.TranscribeChunkAsync(Chunk, null, CancellationToken.None);

        Assert.Equal("https://kaynak.services.ai.azure.com/openai/v1/audio/transcriptions", handler.Url);
        Assert.Equal("az-key", handler.Headers["api-key"]);
        Assert.DoesNotContain("verbose_json", handler.Body);
        Assert.DoesNotContain("name=language", handler.Body);
        var segment = Assert.Single(segments);
        Assert.Equal(Chunk.Duration, segment.End);
    }

    [Fact]
    public async Task OpenAi_speech_writes_the_returned_audio()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "MP3DATA");
        var service = new OpenAiSpeechSynthesizer(new HttpClient(handler), OpenAiEndpoint.OpenAi("sk-test"), "gpt-4o-mini-tts");
        var path = Path.Combine(Path.GetTempPath(), $"tts-{Guid.NewGuid():N}.mp3");
        try
        {
            await service.SynthesizeAsync("Merhaba", "nova", path, CancellationToken.None);

            Assert.Equal("MP3DATA", await File.ReadAllTextAsync(path));
            using var body = JsonDocument.Parse(handler.Body);
            Assert.Equal("nova", body.RootElement.GetProperty("voice").GetString());
            Assert.Equal("gpt-4o-mini-tts", body.RootElement.GetProperty("model").GetString());
            Assert.False(File.Exists(path + ".part"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Azure_speech_transcription_sends_locale_and_reads_phrases()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"durationMilliseconds":4000,"phrases":[{"offsetMilliseconds":500,"durationMilliseconds":1200,"text":"Merhaba."}]}""");
        var service = new AzureSpeechTranscriber(new HttpClient(handler), new AzureSpeechEndpoint("West Europe", "speech-key"));

        var segments = await service.TranscribeChunkAsync(Chunk, "tr", CancellationToken.None);

        Assert.Equal("https://westeurope.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version=2024-11-15", handler.Url);
        Assert.Equal("speech-key", handler.Headers["Ocp-Apim-Subscription-Key"]);
        Assert.Contains("""{"locales":["tr-TR"]}""", handler.Body);
        var segment = Assert.Single(segments);
        Assert.Equal(TimeSpan.FromMilliseconds(500), segment.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(1700), segment.End);
    }

    [Fact]
    public async Task Azure_speech_synthesis_escapes_text_and_reports_errors_in_turkish()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"error":{"code":"401","message":"Access denied"}}""");
        var service = new AzureSpeechSynthesizer(new HttpClient(handler), new AzureSpeechEndpoint("westeurope", "wrong"));

        var error = await Assert.ThrowsAsync<SpeechException>(() =>
            service.SynthesizeAsync("Tom & Jerry <canlı>", "tr-TR-AhmetNeural", Path.GetTempFileName(), CancellationToken.None));

        Assert.Equal("https://westeurope.tts.speech.microsoft.com/cognitiveservices/v1", handler.Url);
        Assert.Contains("<voice name='tr-TR-AhmetNeural'>Tom &amp; Jerry &lt;canlı&gt;</voice>", handler.Body);
        Assert.Contains("xml:lang='tr-TR'", handler.Body);
        Assert.Equal("audio-48khz-192kbitrate-mono-mp3", handler.Headers["X-Microsoft-OutputFormat"]);
        Assert.Contains("API anahtarı geçersiz", error.Message);
        Assert.Contains("Access denied", error.Message);
    }

    [Fact]
    public async Task Missing_key_fails_before_sending()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var service = new OpenAiTranscriber(new HttpClient(handler), OpenAiEndpoint.OpenAi(""), "whisper-1");

        await Assert.ThrowsAsync<SpeechException>(() => service.TranscribeChunkAsync(Chunk, null, CancellationToken.None));
        Assert.Null(handler.Url);
    }

    private sealed class StubHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = string.Join(",", header.Value);
            }

            if (request.Content is not null)
            {
                Body = Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            }

            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }
}
