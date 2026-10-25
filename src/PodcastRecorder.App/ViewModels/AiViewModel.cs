using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Speech;
using PodcastRecorder.Speech.Providers;
using PodcastRecorder.App.Localization;

namespace PodcastRecorder.App.ViewModels;

/// <summary>Transcripts of recordings and spoken announcements generated from text.</summary>
public partial class AiViewModel : ObservableObject, IDisposable
{
    private readonly AiSettings _settings;
    private readonly Func<string> _recordingFolder;
    private readonly PlaylistViewModel _playlist;
    private readonly JinglePadViewModel _jingles;
    private readonly IFileDialogs _dialogs;
    private CancellationTokenSource? _transcription;

    public AiViewModel(AiSettings settings, Func<string> recordingFolder, PlaylistViewModel playlist,
        JinglePadViewModel jingles, IFileDialogs dialogs)
    {
        _settings = settings;
        _recordingFolder = recordingFolder;
        _playlist = playlist;
        _jingles = jingles;
        _dialogs = dialogs;

        TranscriptionProvider = settings.TranscriptionProvider;
        Language = settings.Language;
        TranscribeOnStop = settings.TranscribeOnStop;
        WhisperModel = settings.WhisperModel;
        OpenAiBaseUrl = settings.OpenAiBaseUrl;
        OpenAiKey = Unprotect(settings.OpenAiProtectedKey);
        OpenAiTranscriptionModel = settings.OpenAiTranscriptionModel;
        OpenAiSpeechModel = settings.OpenAiSpeechModel;
        AzureOpenAiEndpoint = settings.AzureOpenAiEndpoint;
        AzureOpenAiKey = Unprotect(settings.AzureOpenAiProtectedKey);
        AzureOpenAiTranscriptionDeployment = settings.AzureOpenAiTranscriptionDeployment;
        AzureOpenAiSpeechDeployment = settings.AzureOpenAiSpeechDeployment;
        AzureSpeechRegion = settings.AzureSpeechRegion;
        AzureSpeechKey = Unprotect(settings.AzureSpeechProtectedKey);
        SynthesisProvider = settings.SynthesisProvider;
    }

    public static IReadOnlyList<SpeechProviderOption> TranscriptionProviders => SpeechProviders.Transcription;

    public static IReadOnlyList<SpeechProviderOption> SynthesisProviders => SpeechProviders.Synthesis;

    public static IReadOnlyList<WhisperModelOption> WhisperModels => LocalWhisperTranscriber.Models;

    public static IReadOnlyList<string> OpenAiTranscriptionModels => OpenAiTranscriber.SuggestedModels;

    public static IReadOnlyList<string> OpenAiSpeechModels => OpenAiSpeechSynthesizer.SuggestedModels;

    public static string WhisperModelFolder => SpeechProviders.WhisperModelFolder;

    // Service settings

    [ObservableProperty]
    public partial string TranscriptionProvider { get; set; }

    /// <summary>ISO code such as "tr"; empty means detect.</summary>
    [ObservableProperty]
    public partial string Language { get; set; }

    [ObservableProperty]
    public partial bool TranscribeOnStop { get; set; }

    [ObservableProperty]
    public partial string WhisperModel { get; set; }

    [ObservableProperty]
    public partial string OpenAiBaseUrl { get; set; }

    [ObservableProperty]
    public partial string OpenAiKey { get; set; }

    [ObservableProperty]
    public partial string OpenAiTranscriptionModel { get; set; }

    [ObservableProperty]
    public partial string OpenAiSpeechModel { get; set; }

    [ObservableProperty]
    public partial string AzureOpenAiEndpoint { get; set; }

    [ObservableProperty]
    public partial string AzureOpenAiKey { get; set; }

    [ObservableProperty]
    public partial string AzureOpenAiTranscriptionDeployment { get; set; }

    [ObservableProperty]
    public partial string AzureOpenAiSpeechDeployment { get; set; }

    [ObservableProperty]
    public partial string AzureSpeechRegion { get; set; }

    [ObservableProperty]
    public partial string AzureSpeechKey { get; set; }

    /// <summary>Whether the "Servis ayarları" section is open.</summary>
    [ObservableProperty]
    public partial bool IsServiceSettingsOpen { get; set; }

    /// <summary>The open tab in "Servis ayarları"; the order of <see cref="ServiceSettingsTabs"/>.</summary>
    [ObservableProperty]
    public partial int ServiceSettingsTab { get; set; }

    /// <summary>Provider ids in the order of the tabs in MainWindow.xaml.</summary>
    private static readonly string[] ServiceSettingsTabs =
    [
        SpeechProviders.LocalWhisper,
        SpeechProviders.OpenAi,
        SpeechProviders.AzureOpenAi,
        SpeechProviders.AzureSpeech,
        SpeechProviders.Windows,
    ];

    /// <summary>Opens "Servis ayarları" on the tab of the given provider.</summary>
    [RelayCommand]
    private void OpenServiceSettings(string? providerId)
    {
        int tab = Array.IndexOf(ServiceSettingsTabs, providerId);
        if (tab >= 0) ServiceSettingsTab = tab;
        IsServiceSettingsOpen = true;
    }

    // Transcription

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranscribeFileCommand), nameof(CancelTranscriptionCommand))]
    public partial bool IsTranscribing { get; set; }

    [ObservableProperty]
    public partial double TranscriptionProgress { get; set; }

    [ObservableProperty]
    public partial string? TranscriptionStatus { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenTranscriptCommand))]
    public partial string? LastTranscriptPath { get; set; }

    // Speech synthesis

    [ObservableProperty]
    public partial string SynthesisProvider { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> VoiceSuggestions { get; set; } = [];

    [ObservableProperty]
    public partial string Voice { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SynthesizeCommand))]
    public partial string SpeechText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SynthesizeCommand))]
    public partial bool IsSynthesizing { get; set; }

    [ObservableProperty]
    public partial string? SynthesisStatus { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSpeechToPlaylistCommand), nameof(AssignSpeechToJingleCommand))]
    public partial string? LastSpeechPath { get; set; }

    partial void OnSynthesisProviderChanged(string value)
    {
        VoiceSuggestions = SpeechProviders.VoicesFor(value);
        Voice = _settings.Voices.TryGetValue(value, out var saved) ? saved : VoiceSuggestions.FirstOrDefault() ?? "";
    }

    partial void OnVoiceChanged(string value) => _settings.Voices[SynthesisProvider] = value;

    /// <summary>Transcribes a recording and saves .txt and .srt next to it.</summary>
    public async Task TranscribeAsync(string audioPath)
    {
        if (IsTranscribing)
        {
            TranscriptionStatus = Loc.T("Ai_TranscriptBusy");
            return;
        }

        IsTranscribing = true;
        TranscriptionProgress = 0;
        TranscriptionStatus = Loc.F("Ai_Preparing", Path.GetFileName(audioPath));
        _transcription = new CancellationTokenSource();
        var token = _transcription.Token;
        var progress = new Progress<TranscriptionProgress>(p =>
        {
            TranscriptionProgress = p.Fraction;
            TranscriptionStatus = Loc.F("Ai_Progress", p.Stage, p.Fraction * 100);
        });
        var language = string.IsNullOrWhiteSpace(Language) ? null : Language.Trim();
        ISpeechToText? service = null;
        try
        {
            service = SpeechProviders.CreateTranscriber(TranscriptionProvider, Credentials());
            var transcript = await Task.Run(() => Transcriber.TranscribeFileAsync(service, audioPath, language, progress, token), token);
            LastTranscriptPath = await transcript.SaveNextToAsync(audioPath, token);
            TranscriptionStatus = transcript.Segments.Count == 0
                ? Loc.F("Ai_NoSpeech", LastTranscriptPath)
                : Loc.F("Ai_TranscriptSaved", LastTranscriptPath);
        }
        catch (OperationCanceledException)
        {
            TranscriptionStatus = Loc.T("Ai_TranscriptCanceled");
        }
        catch (SpeechException ex)
        {
            // Usually a missing key, address or region: show where to enter it.
            TranscriptionStatus = Loc.F("Ai_TranscriptFailed", ex.Message);
            OpenServiceSettings(TranscriptionProvider);
        }
        catch (Exception ex)
        {
            TranscriptionStatus = Loc.F("Ai_TranscriptFailed", ex.Message);
        }
        finally
        {
            (service as IDisposable)?.Dispose();
            _transcription.Dispose();
            _transcription = null;
            IsTranscribing = false;
        }
    }

    private bool CanTranscribe() => !IsTranscribing;

    [RelayCommand(CanExecute = nameof(CanTranscribe))]
    private async Task TranscribeFile()
    {
        if (_dialogs.PickAudioFiles(multiple: false) is [var path])
        {
            await TranscribeAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(IsTranscribing))]
    private void CancelTranscription() => _transcription?.Cancel();

    private bool HasTranscript() => LastTranscriptPath is not null;

    [RelayCommand(CanExecute = nameof(HasTranscript))]
    private void OpenTranscript()
    {
        if (LastTranscriptPath is { } path && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private bool CanSynthesize() => !IsSynthesizing && !string.IsNullOrWhiteSpace(SpeechText);

    /// <summary>Writes the spoken text to the Loc.T("Ai_AnnouncementsFolder") folder under the recording folder.</summary>
    [RelayCommand(CanExecute = nameof(CanSynthesize))]
    private async Task Synthesize()
    {
        var text = SpeechText.Trim();
        IsSynthesizing = true;
        SynthesisStatus = Loc.T("Ai_Synthesizing");
        try
        {
            var service = SpeechProviders.CreateSynthesizer(SynthesisProvider, Credentials());
            if (text.Length > service.MaxCharacters)
            {
                SynthesisStatus = Loc.F("Ai_TextTooLong", service.MaxCharacters);
                return;
            }

            var folder = Path.Combine(_recordingFolder(), Loc.T("Ai_AnnouncementsFolder"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileNameFor(text) + service.FileExtension);
            await service.SynthesizeAsync(text, Voice, path, CancellationToken.None);
            LastSpeechPath = path;
            SynthesisStatus = Loc.F("Ai_SpeechReady", Path.GetFileName(path));
        }
        catch (SpeechException ex)
        {
            // Usually a missing key, address or region: show where to enter it.
            SynthesisStatus = Loc.F("Ai_SynthesisFailed", ex.Message);
            OpenServiceSettings(SynthesisProvider);
        }
        catch (Exception ex)
        {
            SynthesisStatus = Loc.F("Ai_SynthesisFailed", ex.Message);
        }
        finally
        {
            IsSynthesizing = false;
        }
    }

    private bool HasSpeech() => LastSpeechPath is not null;

    [RelayCommand(CanExecute = nameof(HasSpeech))]
    private void AddSpeechToPlaylist()
    {
        if (LastSpeechPath is { } path)
        {
            _playlist.AddFiles([path]);
            SynthesisStatus = Loc.F("Ai_AddedToPlaylist", Path.GetFileName(path));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSpeech))]
    private void AssignSpeechToJingle()
    {
        if (LastSpeechPath is not { } path)
        {
            return;
        }

        var cell = _jingles.SelectedBank?.Cells.FirstOrDefault(c => !c.HasFile);
        if (cell is null)
        {
            SynthesisStatus = Loc.T("Ai_NoEmptyPad");
            return;
        }

        cell.Assign(path);
        SynthesisStatus = cell.Shortcut is { } key
            ? Loc.F("Ai_AssignedToPadKey", key)
            : Loc.T("Ai_AssignedToPad");
    }

    public void SaveTo(AiSettings settings)
    {
        settings.TranscriptionProvider = TranscriptionProvider;
        settings.SynthesisProvider = SynthesisProvider;
        settings.Language = Language;
        settings.TranscribeOnStop = TranscribeOnStop;
        settings.WhisperModel = WhisperModel;
        settings.OpenAiBaseUrl = OpenAiBaseUrl;
        settings.OpenAiProtectedKey = Protect(OpenAiKey);
        settings.OpenAiTranscriptionModel = OpenAiTranscriptionModel;
        settings.OpenAiSpeechModel = OpenAiSpeechModel;
        settings.AzureOpenAiEndpoint = AzureOpenAiEndpoint;
        settings.AzureOpenAiProtectedKey = Protect(AzureOpenAiKey);
        settings.AzureOpenAiTranscriptionDeployment = AzureOpenAiTranscriptionDeployment;
        settings.AzureOpenAiSpeechDeployment = AzureOpenAiSpeechDeployment;
        settings.AzureSpeechRegion = AzureSpeechRegion;
        settings.AzureSpeechProtectedKey = Protect(AzureSpeechKey);
    }

    public void Dispose() => _transcription?.Cancel();

    private static string? Protect(string key) => string.IsNullOrWhiteSpace(key) ? null : Secrets.Protect(key.Trim());

    private static string Unprotect(string? protectedKey) => Secrets.Unprotect(protectedKey) ?? "";

    private SpeechCredentials Credentials() => new(
        WhisperModel,
        OpenAiBaseUrl,
        OpenAiKey,
        OpenAiTranscriptionModel,
        OpenAiSpeechModel,
        AzureOpenAiEndpoint,
        AzureOpenAiKey,
        AzureOpenAiTranscriptionDeployment,
        AzureOpenAiSpeechDeployment,
        AzureSpeechRegion,
        AzureSpeechKey);

    /// <summary>"Şimdi reklam arası" → "anons-20261024-153000 Şimdi reklam arası".</summary>
    private static string FileNameFor(string text)
    {
        var words = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(5));
        var safe = Regex.Replace(words, $"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", "");
        if (safe.Length > 40) safe = safe[..40];
        // Windows drops trailing dots and spaces from names, and "metin." would end in "..wav".
        safe = safe.TrimEnd('.', ' ');
        return $"{Loc.T("Ai_AnnouncementFilePrefix")}-{DateTime.Now:yyyyMMdd-HHmmss} {safe}".TrimEnd();
    }
}
