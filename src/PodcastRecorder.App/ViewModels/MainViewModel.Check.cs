using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Localization;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.App.ViewModels;

/// <summary>One line of the pre-recording check, with a mark for its status.</summary>
public sealed record CheckResultLine(CheckStatus Status, string Text)
{
    public bool IsOk => Status == CheckStatus.Ok;

    public bool IsWarning => Status == CheckStatus.Warning;

    public bool IsProblem => Status == CheckStatus.Problem;

    public string Symbol => Status switch
    {
        CheckStatus.Ok => "✓",
        CheckStatus.Warning => "!",
        _ => "✕",
    };
}

/// <summary>"Ön kontrol": a ten-second test of the microphones, the room and the disk before recording.</summary>
public partial class MainViewModel
{
    private CancellationTokenSource? _checkCancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCheckPanelVisible), nameof(CheckButtonText))]
    public partial bool IsChecking { get; set; }

    /// <summary>What the speaker should do right now ("stay quiet", "speak"), with a countdown.</summary>
    [ObservableProperty]
    public partial string? CheckPrompt { get; set; }

    /// <summary>0 to 1 through the whole test.</summary>
    [ObservableProperty]
    public partial double CheckProgress { get; set; }

    [ObservableProperty]
    public partial string? CheckSummary { get; set; }

    public ObservableCollection<CheckResultLine> CheckResults { get; } = [];

    public bool IsCheckPanelVisible => IsChecking || CheckResults.Count > 0;

    public string CheckButtonText => IsChecking ? Loc.T("Check_Cancel") : Loc.T("Check_Button");

    /// <summary>Starts the test, or cancels it while it runs.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ToggleCheck()
    {
        if (IsChecking) _checkCancel?.Cancel();
        else _ = RunCheckAsync();
    }

    [RelayCommand]
    private void CloseCheck()
    {
        _checkCancel?.Cancel();
        CheckResults.Clear();
        CheckSummary = null;
        OnPropertyChanged(nameof(IsCheckPanelVisible));
    }

    private async Task RunCheckAsync()
    {
        using var cancel = new CancellationTokenSource();
        _checkCancel = cancel;
        CheckResults.Clear();
        CheckSummary = null;
        CheckProgress = 0;
        IsChecking = true;

        var strips = _microphoneStrips.ToList();
        var probes = strips.ToDictionary(s => s, _ => new LevelProbe());
        bool measure = strips.Count > 0 && _engine.IsOutputRunning;
        var results = new List<CheckResultLine>();
        try
        {
            if (measure)
            {
                foreach (var (strip, probe) in probes) strip.Channel.Probe = probe;
                var total = PreRecordingCheck.QuietPhase + PreRecordingCheck.SpeechPhase;

                await CountDownAsync("Check_StayQuiet", PreRecordingCheck.QuietPhase, TimeSpan.Zero, total, cancel.Token);
                var quiet = probes.ToDictionary(p => p.Key, p => p.Value.TakeReading());

                await CountDownAsync("Check_Speak", PreRecordingCheck.SpeechPhase, PreRecordingCheck.QuietPhase, total, cancel.Token);
                var speech = probes.ToDictionary(p => p.Key, p => p.Value.TakeReading());

                foreach (var strip in strips)
                {
                    var prefix = strips.Count > 1 ? $"{strip.Name}: " : "";
                    var check = PreRecordingCheck.AnalyzeMicrophone(quiet[strip], speech[strip]);
                    results.Add(new CheckResultLine(check.LevelStatus, prefix + LevelText(check)));
                    results.Add(new CheckResultLine(check.NoiseStatus, prefix + NoiseText(check)));
                }

                if (!IsMicrophoneLive) results.Add(new CheckResultLine(CheckStatus.Warning, Loc.T("Check_MicOff")));
            }
            else if (strips.Count == 0)
            {
                results.Add(new CheckResultLine(CheckStatus.Problem, Loc.T("Check_NoMicrophone")));
            }
            else
            {
                results.Add(new CheckResultLine(CheckStatus.Problem, Loc.T("Check_NoOutput")));
            }

            results.Add(DiskLine());
        }
        catch (OperationCanceledException)
        {
            results.Clear();
        }
        finally
        {
            foreach (var strip in strips) strip.Channel.Probe = null;
            _checkCancel = null;
            CheckPrompt = null;
            IsChecking = false;
        }

        foreach (var line in results) CheckResults.Add(line);
        int warnings = results.Count(r => r.Status == CheckStatus.Warning);
        int problems = results.Count(r => r.Status == CheckStatus.Problem);
        CheckSummary = results.Count == 0 ? null
            : problems > 0 ? Loc.F("Check_SummaryProblems", problems + warnings)
            : warnings > 0 ? Loc.F("Check_SummaryWarnings", warnings)
            : Loc.T("Check_SummaryOk");
        StatusMessage = CheckSummary ?? Loc.T("Check_Cancelled");
        OnPropertyChanged(nameof(IsCheckPanelVisible));
    }

    /// <summary>Shows <paramref name="promptKey"/> with the seconds left until <paramref name="length"/> has passed.</summary>
    private async Task CountDownAsync(string promptKey, TimeSpan length, TimeSpan before, TimeSpan total, CancellationToken cancel)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            var elapsed = DateTime.UtcNow - started;
            if (elapsed >= length) break;
            CheckPrompt = Loc.F(promptKey, Math.Ceiling((length - elapsed).TotalSeconds));
            CheckProgress = (before + elapsed) / total;
            await Task.Delay(100, cancel);
        }

        CheckProgress = (before + length) / total;
    }

    private string LevelText(MicrophoneCheck check) => check.LevelIssue switch
    {
        LevelIssue.NoSignal => Loc.T("Check_NoSignal") + (OperatingSystem.IsMacOS() ? " " + Loc.T("Check_NoSignalMac") : ""),
        LevelIssue.Clipping => Loc.F("Check_Clipping", check.PeakDb),
        LevelIssue.TooLoud => Loc.F("Check_TooLoud", check.SpeechDb),
        LevelIssue.TooQuiet => Loc.F("Check_TooQuiet", check.SpeechDb),
        _ => Loc.F("Check_LevelOk", check.SpeechDb, check.PeakDb),
    };

    private string NoiseText(MicrophoneCheck check)
    {
        var hint = IsNoiseReductionAvailable && !NoiseReductionEnabled ? " " + Loc.T("Check_TryNoiseReduction") : "";
        return check.NoiseIssue switch
        {
            NoiseIssue.NotMeasured => Loc.T("Check_NoiseNotMeasured"),
            NoiseIssue.Loud => Loc.F("Check_NoiseLoud", check.NoiseDb) + hint,
            NoiseIssue.Noticeable => Loc.F("Check_NoiseNoticeable", check.NoiseDb) + hint,
            NoiseIssue.LowSeparation => Loc.F("Check_LowSeparation", check.SeparationDb),
            _ => Loc.F("Check_NoiseOk", check.NoiseDb, check.SeparationDb),
        };
    }

    private CheckResultLine DiskLine()
    {
        if (PreRecordingCheck.FreeSpaceFor(RecordingFolder) is not { } free)
        {
            return new CheckResultLine(CheckStatus.Warning, Loc.F("Check_DiskUnknown", RecordingFolder));
        }

        var disk = PreRecordingCheck.AnalyzeDisk(free, BytesPerHour());
        var freeText = FormatBytes(free);
        var time = disk.RecordingTime >= TimeSpan.FromHours(100)
            ? Loc.T("Check_DiskPlenty")
            : disk.RecordingTime >= TimeSpan.FromHours(1)
                ? Loc.F("Check_Hours", Math.Floor(disk.RecordingTime.TotalHours))
                : Loc.F("Check_Minutes", Math.Floor(disk.RecordingTime.TotalMinutes));
        var key = disk.Status switch
        {
            CheckStatus.Ok => "Check_DiskOk",
            CheckStatus.Warning => "Check_DiskLow",
            _ => "Check_DiskFull",
        };
        return new CheckResultLine(disk.Status, Loc.F(key, freeText, time));
    }

    /// <summary>What an hour of recording takes with the current settings: the file, tracks, normalizing and backup.</summary>
    private long BytesPerHour()
    {
        long main = EncoderCatalog.EstimatedBytesPerHour(SelectedEncoder.Id);
        long total = main;
        // Normalizing first captures 32-bit float WAV, then writes the final file next to it.
        if (NormalizeOnStop) total += EncoderCatalog.EstimatedBytesPerHour("wav") * 2;
        if (RecordStems) total += main * _engine.Mixer.Channels.Count;
        if (BackupRecording) total += new BackupEncoder().BytesPerHour;
        return total;
    }

    private static string FormatBytes(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.#} GB"
        : $"{bytes / (double)(1L << 20):0} MB";
}
