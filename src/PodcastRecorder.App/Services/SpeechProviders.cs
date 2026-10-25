using System.IO;
using System.Net.Http;
using PodcastRecorder.Speech;
using PodcastRecorder.Speech.Providers;
using PodcastRecorder.App.Localization;

namespace PodcastRecorder.App.Services;

/// <summary>A speech service; <see cref="Name"/> is in the current language.</summary>
public sealed record SpeechProviderOption(string Id, string NameKey)
{
    public string Name => Loc.T(NameKey);
}

/// <summary>The speech services the user can pick, and how to build each from the settings.</summary>
public static class SpeechProviders
{
    public const string LocalWhisper = "local-whisper";
    public const string OpenAi = "openai";
    public const string AzureOpenAi = "azure-openai";
    public const string AzureSpeech = "azure-speech";
    public const string Windows = "windows";

    public static IReadOnlyList<SpeechProviderOption> Transcription { get; } =
    [
        new(LocalWhisper, "Provider_LocalWhisper"),
        new(OpenAi, "Provider_OpenAi"),
        new(AzureOpenAi, "Provider_AzureOpenAi"),
        new(AzureSpeech, "Provider_AzureSpeech"),
    ];

    public static IReadOnlyList<SpeechProviderOption> Synthesis { get; } =
    [
        new(Windows, "Provider_Windows"),
        new(OpenAi, "Provider_OpenAi"),
        new(AzureOpenAi, "Provider_AzureOpenAi"),
        new(AzureSpeech, "Provider_AzureSpeech"),
    ];

    public static string WhisperModelFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PodcastRecorder", "whisper");

    /// <summary>Long recordings upload in 19 MB pieces; give each one time to finish.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static ISpeechToText CreateTranscriber(string provider, SpeechCredentials c) => provider switch
    {
        OpenAi => new OpenAiTranscriber(Http, OpenAiEndpoint.OpenAi(c.OpenAiKey, c.OpenAiBaseUrl), c.OpenAiTranscriptionModel),
        AzureOpenAi => new OpenAiTranscriber(Http, OpenAiEndpoint.Azure(c.AzureOpenAiEndpoint, c.AzureOpenAiKey), c.AzureOpenAiTranscriptionDeployment),
        AzureSpeech => new AzureSpeechTranscriber(Http, new AzureSpeechEndpoint(c.AzureSpeechRegion, c.AzureSpeechKey)),
        _ => new LocalWhisperTranscriber(WhisperModelFolder, c.WhisperModel),
    };

    public static ITextToSpeech CreateSynthesizer(string provider, SpeechCredentials c) => provider switch
    {
        OpenAi => new OpenAiSpeechSynthesizer(Http, OpenAiEndpoint.OpenAi(c.OpenAiKey, c.OpenAiBaseUrl), c.OpenAiSpeechModel),
        AzureOpenAi => new OpenAiSpeechSynthesizer(Http, OpenAiEndpoint.Azure(c.AzureOpenAiEndpoint, c.AzureOpenAiKey), c.AzureOpenAiSpeechDeployment),
        AzureSpeech => new AzureSpeechSynthesizer(Http, new AzureSpeechEndpoint(c.AzureSpeechRegion, c.AzureSpeechKey)),
        _ => new WindowsSpeechSynthesizer(),
    };

    public static IReadOnlyList<string> VoicesFor(string provider) => provider switch
    {
        OpenAi or AzureOpenAi => OpenAiSpeechSynthesizer.Voices,
        AzureSpeech => AzureSpeechSynthesizer.SuggestedVoices,
        _ => InstalledWindowsVoices(),
    };

    private static IReadOnlyList<string> InstalledWindowsVoices()
    {
        try
        {
            return WindowsSpeechSynthesizer.InstalledVoices();
        }
        catch (Exception)
        {
            return [];
        }
    }
}

/// <summary>A snapshot of the service settings, taken on the UI thread before work starts.</summary>
public sealed record SpeechCredentials(
    string WhisperModel,
    string OpenAiBaseUrl,
    string OpenAiKey,
    string OpenAiTranscriptionModel,
    string OpenAiSpeechModel,
    string AzureOpenAiEndpoint,
    string AzureOpenAiKey,
    string AzureOpenAiTranscriptionDeployment,
    string AzureOpenAiSpeechDeployment,
    string AzureSpeechRegion,
    string AzureSpeechKey);
