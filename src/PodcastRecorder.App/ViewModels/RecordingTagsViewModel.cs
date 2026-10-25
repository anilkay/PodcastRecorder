using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.App.Localization;

namespace PodcastRecorder.App.ViewModels;

/// <summary>
/// "Dosya bilgileri": the title, artist, album, episode number, cover and so on written into each
/// finished recording, its separate tracks, or any audio file the user picks.
/// </summary>
public partial class RecordingTagsViewModel : ObservableObject
{
    private readonly RecordingTagsSettings _settings;
    private readonly IFileDialogs _dialogs;
    private readonly Func<string?> _lastRecording;
    private readonly Action<string> _report;

    /// <summary>The episode number the last recording got; the field has moved on to the next one.</summary>
    private uint? _lastRecordingEpisode;

    public RecordingTagsViewModel(RecordingTagsSettings settings, IFileDialogs dialogs, Func<string?> lastRecording, Action<string> report)
    {
        _settings = settings;
        _dialogs = dialogs;
        _lastRecording = lastRecording;
        _report = report;
        Artist = settings.Artist;
        Album = settings.Album;
        Genre = settings.Genre;
        CoverImagePath = settings.CoverImagePath is { } cover && File.Exists(cover) ? cover : null;
        EpisodeText = settings.NextEpisode.ToString();
        YearText = DateTime.Now.Year.ToString();
    }

    /// <summary>Empty means the file name is used.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Artist { get; set; }

    [ObservableProperty]
    public partial string Album { get; set; }

    /// <summary>The episode number the next recording gets; it goes up by one after each recording.</summary>
    [ObservableProperty]
    public partial string EpisodeText { get; set; }

    [ObservableProperty]
    public partial string YearText { get; set; }

    [ObservableProperty]
    public partial string Genre { get; set; }

    [ObservableProperty]
    public partial string Comment { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    [NotifyCanExecuteChangedFor(nameof(ClearCoverCommand))]
    public partial string? CoverImagePath { get; set; }

    public bool HasCover => CoverImagePath is not null;

    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>
    /// Writes the tags into a finished recording and its separate tracks (title suffixed with the
    /// track name), then moves on to the next episode number. Returns the result for the status
    /// bar, where the caller puts it after the "saved" message.
    /// </summary>
    public async Task<string> ApplyToRecordingAsync(string path, IReadOnlyList<string> stemPaths)
    {
        var tags = TagsFor(path);
        _lastRecordingEpisode = tags.Track;
        try
        {
            await Task.Run(() =>
            {
                TagWriter.Write(path, tags);
                foreach (var stem in stemPaths.Where(File.Exists))
                {
                    TagWriter.Write(stem, tags with { Title = $"{tags.Title} - {Path.GetFileNameWithoutExtension(stem)}" });
                }
            });
        }
        catch (IOException ex)
        {
            return Status = ex.Message;
        }

        if (tags.Track is { } episode) EpisodeText = (episode + 1).ToString();
        return Status = tags.Track is { } track ? Loc.F("Tags_WrittenEpisode", track) : Loc.T("Tags_Written");
    }

    private bool HasLastRecording() => _lastRecording() is { } path && File.Exists(path);

    /// <summary>
    /// Writes the current fields into the last recording again, e.g. after fixing a typo. It keeps
    /// the episode number that recording got, not the next one now in the field.
    /// </summary>
    [RelayCommand]
    private async Task WriteToLastRecording()
    {
        if (!HasLastRecording())
        {
            Report(Loc.T("Tags_NoRecordingYet"));
            return;
        }

        var path = _lastRecording()!;
        await WriteAsync(path, TagsFor(path) with { Track = _lastRecordingEpisode });
    }

    /// <summary>Writes the current fields into any audio file.</summary>
    [RelayCommand]
    private async Task WriteToFile()
    {
        if (_dialogs.PickAudioFiles(multiple: false) is [var path])
        {
            await WriteAsync(path, TagsFor(path));
        }
    }

    [RelayCommand]
    private void ChooseCover()
    {
        if (_dialogs.PickImage() is { } path) CoverImagePath = path;
    }

    [RelayCommand(CanExecute = nameof(HasCover))]
    private void ClearCover() => CoverImagePath = null;

    public void SaveTo(RecordingTagsSettings settings)
    {
        settings.Artist = Artist.Trim();
        settings.Album = Album.Trim();
        settings.Genre = Genre.Trim();
        settings.CoverImagePath = CoverImagePath;
        settings.NextEpisode = ParseNumber(EpisodeText) ?? settings.NextEpisode;
    }

    private async Task WriteAsync(string path, RecordingTags tags)
    {
        try
        {
            await Task.Run(() => TagWriter.Write(path, tags));
            Report(Loc.F("Tags_WrittenTo", Path.GetFileName(path)));
        }
        catch (IOException ex)
        {
            Report(ex.Message);
        }
    }

    private RecordingTags TagsFor(string path) => new(
        Title: string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(path) : Title,
        Artist: Artist,
        Album: Album,
        Track: ParseNumber(EpisodeText),
        Year: ParseNumber(YearText),
        Genre: Genre,
        Comment: Comment,
        CoverImagePath: CoverImagePath);

    private static uint? ParseNumber(string? text) => uint.TryParse(text?.Trim(), out var n) && n > 0 ? n : null;

    private void Report(string message)
    {
        Status = message;
        _report(message);
    }
}
