using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App.ViewModels;

public partial class JingleCellViewModel(int index, JinglePadViewModel pad) : ObservableObject
{
    public int Index { get; } = index;

    public string Key => $"pad-{Index}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(HasFile))]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    public bool HasFile => FilePath is not null;

    public string Title => FilePath is null ? "Boş" : Path.GetFileNameWithoutExtension(FilePath);

    public void Assign(string path)
    {
        if (!AudioFiles.IsSupported(path)) return;
        pad.Stop(this);
        FilePath = path;
    }

    [RelayCommand]
    private void Toggle() => pad.Toggle(this);

    [RelayCommand]
    private void Browse()
    {
        if (pad.PickFile() is { } path) Assign(path);
    }

    [RelayCommand]
    private void Clear()
    {
        pad.Stop(this);
        FilePath = null;
    }
}

/// <summary>The jingle cart: a fixed grid of pads, each holding one file.</summary>
public partial class JinglePadViewModel : ObservableObject
{
    public const int Rows = 4;
    public const int Columns = 4;

    private readonly JinglePlayer _player;
    private readonly IFileDialogs _dialogs;

    public JinglePadViewModel(JinglePlayer player, IFileDialogs dialogs, SynchronizationContext ui)
    {
        _player = player;
        _dialogs = dialogs;
        for (int i = 0; i < Rows * Columns; i++)
        {
            Cells.Add(new JingleCellViewModel(i, this));
        }

        _player.JingleEnded += (_, key) => ui.Post(_ => MarkStopped(key), null);
    }

    public ObservableCollection<JingleCellViewModel> Cells { get; } = [];

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public void Toggle(JingleCellViewModel cell)
    {
        if (cell.FilePath is null) return;

        if (cell.IsPlaying)
        {
            Stop(cell);
            return;
        }

        try
        {
            _player.Play(cell.Key, cell.FilePath);
            cell.IsPlaying = true;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Jingle çalınamadı: {cell.Title} ({ex.Message})";
        }
    }

    public void Stop(JingleCellViewModel cell)
    {
        _player.Stop(cell.Key);
        cell.IsPlaying = false;
    }

    [RelayCommand]
    private void StopAll()
    {
        _player.StopAll();
        foreach (var cell in Cells) cell.IsPlaying = false;
    }

    internal string? PickFile() => _dialogs.PickAudioFiles(multiple: false).FirstOrDefault();

    private void MarkStopped(string key)
    {
        var cell = Cells.FirstOrDefault(c => c.Key == key);
        if (cell is not null && !_player.IsPlaying(key)) cell.IsPlaying = false;
    }
}
