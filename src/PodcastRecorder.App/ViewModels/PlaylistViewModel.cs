using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App.ViewModels;

public partial class PlaylistItemViewModel(string filePath) : ObservableObject
{
    public string FilePath { get; } = filePath;

    public string Title { get; } = Path.GetFileNameWithoutExtension(filePath);

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }
}

public partial class PlaylistViewModel : ObservableObject
{
    private readonly PlaybackDeck _deck;
    private readonly IFileDialogs _dialogs;
    private readonly SynchronizationContext _ui;

    public PlaylistViewModel(PlaybackDeck deck, IFileDialogs dialogs, SynchronizationContext ui)
    {
        _deck = deck;
        _dialogs = dialogs;
        _ui = ui;
        _deck.PlaybackEnded += (_, path) => _ui.Post(_ => OnPlaybackEnded(path), null);
        _deck.TrackEnding += (_, path) => _ui.Post(_ => OnTrackEnding(path), null);
    }

    public ObservableCollection<PlaylistItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial PlaylistItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial PlaylistItemViewModel? CurrentItem { get; set; }

    [ObservableProperty]
    public partial bool AutoAdvance { get; set; } = true;

    /// <summary>Overlap between songs in seconds; 0 turns crossfade off.</summary>
    [ObservableProperty]
    public partial double CrossfadeSeconds { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string PositionText { get; set; } = "00:00 / 00:00";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(AudioFiles.IsSupported))
        {
            Items.Add(new PlaylistItemViewModel(path));
        }
    }

    [RelayCommand]
    private void Browse() => AddFiles(_dialogs.PickAudioFiles(multiple: true));

    [RelayCommand]
    private void Play(PlaylistItemViewModel? item)
    {
        item ??= SelectedItem ?? Items.FirstOrDefault();
        if (item is null) return;

        try
        {
            _deck.Play(item.FilePath);
            SetCurrent(item);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Çalınamadı: {item.Title} ({ex.Message})";
            SetCurrent(null);
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _deck.Stop();
        SetCurrent(null);
    }

    [RelayCommand]
    private void Next()
    {
        var next = NextAfter(CurrentItem ?? SelectedItem);
        if (next is null) Stop();
        else Play(next);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (SelectedItem is null) return;
        if (SelectedItem == CurrentItem) Stop();
        Items.Remove(SelectedItem);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => Move(+1);

    [RelayCommand]
    private void Clear()
    {
        Stop();
        Items.Clear();
    }

    /// <summary>Called by the UI timer to update the progress bar.</summary>
    public void Refresh()
    {
        if (CurrentItem is null)
        {
            Progress = 0;
            PositionText = "00:00 / 00:00";
            return;
        }

        var position = _deck.Position;
        var duration = _deck.Duration;
        Progress = duration > TimeSpan.Zero ? position / duration : 0;
        PositionText = $"{position:mm\\:ss} / {duration:mm\\:ss}";
    }

    private bool HasSelection() => SelectedItem is not null;

    private void Move(int delta)
    {
        if (SelectedItem is null) return;
        int from = Items.IndexOf(SelectedItem);
        int to = from + delta;
        if (to < 0 || to >= Items.Count) return;
        Items.Move(from, to);
    }

    private PlaylistItemViewModel? NextAfter(PlaylistItemViewModel? item)
    {
        if (item is null) return Items.FirstOrDefault();
        int index = Items.IndexOf(item);
        return index >= 0 && index + 1 < Items.Count ? Items[index + 1] : null;
    }

    partial void OnCrossfadeSecondsChanged(double value) => _deck.CrossfadeDuration = TimeSpan.FromSeconds(value);

    /// <summary>With crossfade on, the next song starts over the tail of the current one.</summary>
    private void OnTrackEnding(string path)
    {
        if (AutoAdvance && CurrentItem?.FilePath == path && NextAfter(CurrentItem) is { } next)
        {
            Play(next);
        }
    }

    private void OnPlaybackEnded(string path)
    {
        // A crossfade may already have moved on to the next song.
        if (CurrentItem?.FilePath != path) return;

        var finished = CurrentItem;
        SetCurrent(null);
        if (AutoAdvance && NextAfter(finished) is { } next)
        {
            Play(next);
        }
    }

    private void SetCurrent(PlaylistItemViewModel? item)
    {
        if (CurrentItem is not null) CurrentItem.IsPlaying = false;
        CurrentItem = item;
        if (item is not null) item.IsPlaying = true;
    }
}
