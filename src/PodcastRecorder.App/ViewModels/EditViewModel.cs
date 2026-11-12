using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Localization;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio;
using PodcastRecorder.Audio.Editing;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Sources;
using PodcastRecorder.Speech;
using PodcastRecorder.Speech.Fillers;

namespace PodcastRecorder.App.ViewModels;

/// <summary>One found cut in the "Düzenle" list.</summary>
public partial class CutItemViewModel(CutRegion region, EditViewModel owner) : ObservableObject
{
    public CutRegion Region { get; } = region;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = region.Enabled;

    partial void OnIsEnabledChanged(bool value) => owner.CutsChanged();

    public bool IsSilence => Region.Kind == CutKind.Silence;

    public string StartText => EditViewModel.FormatTime(Region.Start);

    public string DurationText => Loc.F("Edit_SecondsShort", Region.Duration.TotalSeconds);

    public string KindName => Loc.T(Region.Kind switch
    {
        CutKind.Silence => "Edit_KindSilence",
        CutKind.Repeat => "Edit_KindRepeat",
        _ => "Edit_KindFiller",
    });

    /// <summary>What was heard: the filler as Whisper wrote it, or the level of the pause.</summary>
    public string Label => Region.Label;

    /// <summary>Whisper's confidence for a filler; empty for a silence.</summary>
    public string ConfidenceText => IsSilence ? "" : Loc.F("Edit_Confidence", Region.Confidence * 100);

    [RelayCommand]
    private Task PlayOriginal() => owner.PreviewAsync(this, withCut: false);

    [RelayCommand]
    private Task PlayEdited() => owner.PreviewAsync(this, withCut: true);
}

/// <summary>
/// "Düzenle": finds long silences and filler sounds in a recording, lets the user pick which go,
/// previews them and writes an edited copy next to the original (and its tracks, in sync).
/// </summary>
public partial class EditViewModel : ObservableObject, IDisposable
{
    private readonly EditSettings _settings;
    private readonly Func<string> _recordingFolder;
    private readonly Func<string> _whisperModel;
    private readonly Func<string> _language;
    private readonly Func<bool> _isOutputRunning;
    private readonly PreviewPlayer _preview;
    private readonly IFileDialogs _dialogs;
    private readonly SynchronizationContext _ui;
    private CancellationTokenSource? _work;
    private TimeSpan _length;
    private bool _bulkChange;

    public EditViewModel(EditSettings settings, Func<string> recordingFolder, Func<string> whisperModel, Func<string> language,
        PreviewPlayer preview, Func<bool> isOutputRunning, IFileDialogs dialogs, SynchronizationContext ui)
    {
        _settings = settings;
        _recordingFolder = recordingFolder;
        _whisperModel = whisperModel;
        _language = language;
        _preview = preview;
        _isOutputRunning = isOutputRunning;
        _dialogs = dialogs;
        _ui = ui;

        SilenceThresholdDb = settings.SilenceThresholdDb;
        MinimumSilenceSeconds = settings.MinimumSilenceSeconds;
        KeptGapSeconds = settings.KeptGapSeconds;
        DetectFillers = settings.DetectFillers;
        FillerWords = settings.FillerWords;
        IncludeStems = settings.IncludeStems;
        ReduceNoise = settings.ReduceNoise && IsNoiseReductionAvailable;
        DetectRepeats = settings.DetectRepeats;
        NormalizeLoudness = settings.NormalizeLoudness;
        IntroPath = ExistingFile(settings.IntroPath);
        OutroPath = ExistingFile(settings.OutroPath);
        SelectedAutoEncoder = EncoderCatalog.Find(settings.AutoEncoderId);
        _preview.Ended += (_, _) => _ui.Post(_ => IsPreviewing = false, null);
    }

    public static IReadOnlyList<EncoderOption> Encoders => EncoderCatalog.All;

    public ObservableCollection<CutItemViewModel> Cuts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceName), nameof(HasSource))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(AutoEditCommand))]
    public partial string? SourcePath { get; set; }

    public string SourceName => SourcePath is null ? Loc.T("Edit_NoRecording") : Path.GetFileName(SourcePath);

    public bool HasSource => SourcePath is not null;

    /// <summary>The separate tracks recorded with <see cref="SourcePath"/>, if it was a multitrack recording.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStems), nameof(StemsText))]
    public partial IReadOnlyList<string> Stems { get; set; } = [];

    public bool HasStems => Stems.Count > 0;

    public string StemsText => Loc.F("Edit_IncludeStems", Stems.Count);

    [ObservableProperty]
    public partial bool IncludeStems { get; set; }

    /// <summary>Also clean the edited copy with RNNoise; with no cuts ticked, the copy is only cleaned.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool ReduceNoise { get; set; }

    public bool IsNoiseReductionAvailable => AudioEngine.IsNoiseReductionAvailable;

    public bool CanChangeReduceNoise => IsIdle && IsNoiseReductionAvailable;

    public string ReduceNoiseTip => Loc.T(IsNoiseReductionAvailable ? "Edit_ReduceNoiseTip" : "Main_NoiseReductionUnavailable");

    [ObservableProperty]
    public partial double SilenceThresholdDb { get; set; }

    [ObservableProperty]
    public partial double MinimumSilenceSeconds { get; set; }

    [ObservableProperty]
    public partial double KeptGapSeconds { get; set; }

    [ObservableProperty]
    public partial bool DetectFillers { get; set; }

    [ObservableProperty]
    public partial string FillerWords { get; set; }

    /// <summary>Also look for phrases said twice in a row and words started over.</summary>
    [ObservableProperty]
    public partial bool DetectRepeats { get; set; }

    /// <summary>Automatic edit: bring the speech to the podcast loudness.</summary>
    [ObservableProperty]
    public partial bool NormalizeLoudness { get; set; }

    /// <summary>Automatic edit: music before the recording, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntroName), nameof(HasIntro))]
    public partial string? IntroPath { get; set; }

    public string IntroName => IntroPath is null ? Loc.T("Edit_NoMusic") : Path.GetFileName(IntroPath);

    public bool HasIntro => IntroPath is not null;

    /// <summary>Automatic edit: music after the recording, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutroName), nameof(HasOutro))]
    public partial string? OutroPath { get; set; }

    public string OutroName => OutroPath is null ? Loc.T("Edit_NoMusic") : Path.GetFileName(OutroPath);

    public bool HasOutro => OutroPath is not null;

    /// <summary>Format of the file the automatic edit writes.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AutoEditCommand))]
    public partial EncoderOption? SelectedAutoEncoder { get; set; }

    /// <summary>The automatic edit is running (it analyses, then writes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle), nameof(CanChangeReduceNoise))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(ExportCommand), nameof(CancelCommand), nameof(ChooseFileCommand), nameof(AutoEditCommand), nameof(ChooseIntroCommand), nameof(ChooseOutroCommand))]
    public partial bool IsAutoEditing { get; set; }

    [ObservableProperty]
    public partial EncoderOption? SelectedEncoder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle), nameof(CanChangeReduceNoise))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(ExportCommand), nameof(CancelCommand), nameof(ChooseFileCommand), nameof(AutoEditCommand), nameof(ChooseIntroCommand), nameof(ChooseOutroCommand))]
    public partial bool IsAnalyzing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle), nameof(CanChangeReduceNoise))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(ExportCommand), nameof(CancelCommand), nameof(ChooseFileCommand), nameof(AutoEditCommand), nameof(ChooseIntroCommand), nameof(ChooseOutroCommand))]
    public partial bool IsExporting { get; set; }

    public bool IsBusy => IsAnalyzing || IsExporting || IsAutoEditing;

    /// <summary>Settings can change only while nothing is running.</summary>
    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopPreviewCommand))]
    public partial bool IsPreviewing { get; set; }

    /// <summary>"12 cuts · 1:23 shorter (45:10 → 43:47)".</summary>
    [ObservableProperty]
    public partial string? SummaryText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealExportCommand))]
    public partial string? LastExportPath { get; set; }

    /// <summary>The recording the cuts in <see cref="Cuts"/> were found in.</summary>
    private string? AnalyzedPath { get; set; }

    partial void OnSourcePathChanged(string? value)
    {
        Cuts.Clear();
        AnalyzedPath = null;
        SummaryText = null;
        Stems = value is null ? [] : FindStems(value);
        SelectedEncoder = DefaultEncoder(value);
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Picks the newest recording in the recording folder, so the tab opens ready to use.</summary>
    public void Initialize()
    {
        if (SourcePath is null) SourcePath = LatestRecording(_recordingFolder());
    }

    /// <summary>A recording just finished: edit it next, unless the user is busy with another one.</summary>
    public void UseRecording(string? path)
    {
        if (path is not null && File.Exists(path) && !IsBusy) SourcePath = path;
    }

    private bool CanChooseFile() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChooseFile))]
    private void ChooseFile()
    {
        if (_dialogs.PickAudioFiles(multiple: false) is [var path]) SourcePath = path;
    }

    private bool CanAnalyze() => SourcePath is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task Analyze()
    {
        if (SourcePath is { } path) await FindCutsAsync(path);
    }

    /// <summary>Fills <see cref="Cuts"/> with what can go from the recording.</summary>
    /// <returns>False if it failed or was cancelled; <see cref="Status"/> then says why.</returns>
    private async Task<bool> FindCutsAsync(string path)
    {
        if (!File.Exists(path))
        {
            Status = Loc.F("Edit_FileMissing", path);
            return false;
        }

        StopPreview();
        IsAnalyzing = true;
        Cuts.Clear();
        SummaryText = null;
        Progress = 0;
        Status = Loc.T("Edit_FindingSilences");
        _work = new CancellationTokenSource();
        var token = _work.Token;
        var options = new SilenceOptions
        {
            ThresholdDb = SilenceThresholdDb,
            MinimumSilence = TimeSpan.FromSeconds(MinimumSilenceSeconds),
            KeptGap = TimeSpan.FromSeconds(KeptGapSeconds),
        };
        bool repeats = DetectRepeats;
        // Fillers and repeats both come out of the same Whisper pass.
        bool fillers = DetectFillers || repeats;
        bool keepFillers = DetectFillers;
        var fillerList = FillerMatcher.ParseList(FillerWords);
        // Silence detection is quick; most of the time goes to Whisper.
        double silenceShare = fillers ? 0.1 : 1;
        var silenceProgress = new Progress<double>(p => Progress = p * silenceShare);
        var fillerProgress = new Progress<TranscriptionProgress>(p =>
        {
            Progress = silenceShare + p.Fraction * (1 - silenceShare);
            Status = Loc.F("Ai_Progress", p.Stage, p.Fraction * 100);
        });

        try
        {
            var (length, silences) = await Task.Run(() =>
            {
                TimeSpan total;
                using (var reader = AudioFiles.Open(path)) total = reader.TotalTime;
                return (total, SilenceDetector.DetectFile(path, options, silenceProgress, token));
            }, token);
            var found = silences.ToList();

            if (fillers)
            {
                var model = _whisperModel();
                var language = string.IsNullOrWhiteSpace(_language()) ? "tr" : _language().Trim();
                using var detector = new FillerDetector(SpeechProviders.WhisperModelFolder, model);
                var result = await Task.Run(() => detector.DetectFileAsync(path, fillerList, language, progress: fillerProgress, cancellationToken: token), token);
                if (keepFillers) found.AddRange(result.Fillers);
                if (repeats) found.AddRange(RepeatMatcher.Match(result.Words, fillerList));
            }

            _length = length;
            AnalyzedPath = path;
            foreach (var region in found.OrderBy(r => r.Start)) Cuts.Add(new CutItemViewModel(region, this));
            CutsChanged();
            Status = Cuts.Count == 0
                ? Loc.T("Edit_NothingFound")
                : Loc.F("Edit_Found", found.Count(r => r.Kind == CutKind.Silence), found.Count(r => r.Kind == CutKind.Filler),
                    found.Count(r => r.Kind == CutKind.Repeat));
            return true;
        }
        catch (OperationCanceledException)
        {
            Cuts.Clear();
            Status = Loc.T("Edit_Canceled");
            return false;
        }
        catch (Exception ex)
        {
            Status = Loc.F("Edit_AnalyzeFailed", ex.Message);
            return false;
        }
        finally
        {
            _work.Dispose();
            _work = null;
            IsAnalyzing = false;
            Progress = 0;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _work?.Cancel();

    [RelayCommand]
    private void SelectSilences(string? on) => SetKind(CutKind.Silence, on == "true");

    [RelayCommand]
    private void SelectFillers(string? on) => SetKind(CutKind.Filler, on == "true");

    /// <summary>Repeats go with the fillers: both are found by Whisper.</summary>
    private void SetKind(CutKind kind, bool enabled)
    {
        _bulkChange = true;
        foreach (var cut in Cuts.Where(c => (c.Region.Kind == CutKind.Silence) == (kind == CutKind.Silence))) cut.IsEnabled = enabled;
        _bulkChange = false;
        CutsChanged();
    }

    /// <summary>A box was ticked or cleared: update the time saved.</summary>
    internal void CutsChanged()
    {
        if (_bulkChange) return;
        ExportCommand.NotifyCanExecuteChanged();
        if (AnalyzedPath is null)
        {
            SummaryText = null;
            return;
        }

        var picked = Regions();
        var removed = CutList.TotalRemoved(picked, _length);
        SummaryText = Loc.F("Edit_Summary", picked.Count, Cuts.Count, FormatTime(removed), FormatTime(_length), FormatTime(_length - removed));
    }

    private List<CutRegion> Regions() => Cuts.Where(c => c.IsEnabled).Select(c => c.Region with { Enabled = true }).ToList();

    /// <summary>Needs cuts to make, or noise reduction on (which works without analysing first).</summary>
    private bool CanExport() => !IsBusy && SelectedEncoder is not null
        && ((AnalyzedPath is not null && Cuts.Any(c => c.IsEnabled)) || (ReduceNoise && SourcePath is not null));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        if (SelectedEncoder is not { } format) return;
        // Cuts belong to the analysed file; a clean-only export works on whatever file is picked.
        bool withCuts = AnalyzedPath is not null && AnalyzedPath == SourcePath && Cuts.Any(c => c.IsEnabled);
        if ((withCuts ? AnalyzedPath : SourcePath) is not { } path) return;
        bool reduceNoise = ReduceNoise && IsNoiseReductionAvailable;

        StopPreview();
        IsExporting = true;
        Progress = 0;
        Status = Loc.T("Edit_Exporting");
        _work = new CancellationTokenSource();
        var token = _work.Token;
        var progress = new Progress<double>(p =>
        {
            Progress = p;
            Status = Loc.F("Edit_ExportProgress", p * 100);
        });

        var sources = IncludeStems ? [path, .. Stems] : new List<string> { path };
        var tracks = sources.Select(source =>
        {
            var encoder = format.Create();
            return new EditTrack(source, new RecordingTarget(EditExporter.EditedPath(source, encoder.FileExtension), encoder), reduceNoise);
        }).ToList();

        try
        {
            var written = await EditExporter.ExportAsync(tracks, withCuts ? Regions() : [], progress: progress, cancellationToken: token);
            LastExportPath = written[0];
            Status = written.Count > 1
                ? Loc.F("Edit_ExportedWithStems", written[0], written.Count - 1)
                : Loc.F("Edit_Exported", written[0]);
        }
        catch (OperationCanceledException)
        {
            Status = Loc.T("Edit_ExportCanceled");
        }
        catch (Exception ex)
        {
            Status = Loc.F("Edit_ExportFailed", ex.Message);
        }
        finally
        {
            // Tracks the export never reached still hold an unopened encoder.
            foreach (var track in tracks) track.Output.Encoder.Dispose();
            _work.Dispose();
            _work = null;
            IsExporting = false;
            Progress = 0;
        }
    }

    private bool CanAutoEdit() => SourcePath is not null && !IsBusy && SelectedAutoEncoder is not null;

    /// <summary>
    /// The one-button edit: finds the cuts, takes all of them out without asking, cleans and
    /// levels the recording, puts the intro and outro around it and writes a file ready to publish.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAutoEdit))]
    private async Task AutoEdit()
    {
        if (SourcePath is not { } path || SelectedAutoEncoder is not { } format) return;

        IsAutoEditing = true;
        try
        {
            if (!await FindCutsAsync(path)) return;

            var options = new MasterOptions
            {
                ReduceNoise = ReduceNoise && IsNoiseReductionAvailable,
                Normalize = NormalizeLoudness,
                IntroPath = ExistingFile(IntroPath),
                OutroPath = ExistingFile(OutroPath),
            };
            var regions = Regions();
            IsExporting = true;
            Progress = 0;
            Status = Loc.T("Edit_AutoWriting");
            _work = new CancellationTokenSource();
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                Status = Loc.F("Edit_AutoProgress", p * 100);
            });
            var encoder = format.Create();
            var target = new RecordingTarget(EditExporter.EditedPath(path, encoder.FileExtension, EpisodeMaster.Suffix), encoder);
            // Progress<T> posts its reports, so one can still arrive after the export is over.
            string? done = null;
            try
            {
                var result = await EpisodeMaster.ExportAsync(path, target, regions, options, progress, _work.Token);
                LastExportPath = result.Path;
                var loudness = !double.IsFinite(result.MeasuredLufs)
                    ? ""
                    : options.Normalize
                        ? Loc.F("Edit_AutoLoudness", result.MeasuredLufs, result.MeasuredLufs + result.GainDb)
                        : Loc.F("Edit_AutoLoudnessKept", result.MeasuredLufs);
                done = Loc.F("Edit_AutoDone", result.Path, regions.Count, FormatTime(result.Removed), FormatTime(result.Length), loudness);
            }
            catch (OperationCanceledException)
            {
                done = Loc.T("Edit_ExportCanceled");
            }
            catch (Exception ex)
            {
                done = Loc.F("Edit_ExportFailed", ex.Message);
            }
            finally
            {
                encoder.Dispose();
                _work.Dispose();
                _work = null;
                IsExporting = false;
                Progress = 0;
                // After any report still queued on the UI thread.
                _ui.Post(_ => Status = done, null);
            }
        }
        finally
        {
            IsAutoEditing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ChooseIntro()
    {
        if (_dialogs.PickAudioFiles(multiple: false) is [var path]) IntroPath = path;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ChooseOutro()
    {
        if (_dialogs.PickAudioFiles(multiple: false) is [var path]) OutroPath = path;
    }

    [RelayCommand]
    private void ClearIntro() => IntroPath = null;

    [RelayCommand]
    private void ClearOutro() => OutroPath = null;

    private static string? ExistingFile(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    private bool HasExport() => LastExportPath is not null;

    [RelayCommand(CanExecute = nameof(HasExport))]
    private void RevealExport()
    {
        if (LastExportPath is { } path) MainViewModel.RevealInFolder(Path.GetDirectoryName(path) ?? "", path);
    }

    /// <summary>Plays a few seconds around <paramref name="item"/> on the headphones, with or without the cut.</summary>
    internal async Task PreviewAsync(CutItemViewModel item, bool withCut)
    {
        if (AnalyzedPath is not { } path) return;
        if (!_isOutputRunning())
        {
            Status = Loc.T("Edit_NeedOutput");
            return;
        }

        try
        {
            var cut = new TimeRange(item.Region.Start, item.Region.End);
            var clip = await Task.Run(() => EditPreview.Render(path, cut, withCut));
            _preview.Play(clip);
            IsPreviewing = true;
        }
        catch (Exception ex)
        {
            Status = Loc.F("Edit_PreviewFailed", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsPreviewing))]
    private void StopPreview()
    {
        _preview.Stop();
        IsPreviewing = false;
    }

    public void SaveTo(EditSettings settings)
    {
        settings.SilenceThresholdDb = SilenceThresholdDb;
        settings.MinimumSilenceSeconds = MinimumSilenceSeconds;
        settings.KeptGapSeconds = KeptGapSeconds;
        settings.DetectFillers = DetectFillers;
        settings.FillerWords = FillerWords;
        settings.IncludeStems = IncludeStems;
        if (IsNoiseReductionAvailable) settings.ReduceNoise = ReduceNoise;
        settings.EncoderId = SelectedEncoder?.Id ?? settings.EncoderId;
        settings.DetectRepeats = DetectRepeats;
        settings.NormalizeLoudness = NormalizeLoudness;
        settings.IntroPath = IntroPath;
        settings.OutroPath = OutroPath;
        settings.AutoEncoderId = SelectedAutoEncoder?.Id ?? settings.AutoEncoderId;
    }

    public void Dispose()
    {
        _work?.Cancel();
        _preview.Stop();
    }

    /// <summary>The saved choice, else the recording's own format (WAV, FLAC), else lossless FLAC.</summary>
    private EncoderOption DefaultEncoder(string? path)
    {
        if (_settings.EncoderId is { } saved && Encoders.FirstOrDefault(e => e.Id == saved) is { } option) return option;
        var id = Path.GetExtension(path ?? "").ToLowerInvariant() == ".wav" ? "wav" : "flac";
        return EncoderCatalog.Find(id);
    }

    /// <summary>The newest recording in the folder that is not itself an edit.</summary>
    internal static string? LatestRecording(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        return new DirectoryInfo(folder).EnumerateFiles()
            .Where(f => AudioFiles.IsSupported(f.Name) && !IsEdit(f.Name) && !f.Name.Contains(".tmp.", StringComparison.Ordinal)
                && !f.Name.EndsWith(".kayit.wav", StringComparison.Ordinal))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// The tracks of a multitrack recording: the files in the folder named after it plus a suffix
    /// ("podcast-… izler", or "… tracks" when recorded in English).
    /// </summary>
    internal static IReadOnlyList<string> FindStems(string recording)
    {
        var folder = Path.GetDirectoryName(recording);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return [];
        var name = Path.GetFileNameWithoutExtension(recording);
        var stems = Directory.EnumerateDirectories(folder, name + " *").FirstOrDefault();
        if (stems is null) return [];
        return Directory.EnumerateFiles(stems)
            .Where(f => AudioFiles.IsSupported(f) && !IsEdit(f))
            .Order(StringComparer.CurrentCulture)
            .ToList();
    }

    private static bool IsEdit(string path) =>
        Path.GetFileNameWithoutExtension(path) is var name
        && (name.Contains(EditExporter.Suffix, StringComparison.Ordinal) || name.Contains(EpisodeMaster.Suffix, StringComparison.Ordinal));

    /// <summary>"1:02:03.4" or "02:03.4".</summary>
    internal static string FormatTime(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss\.f" : @"mm\:ss\.f", CultureInfo.InvariantCulture);
}
