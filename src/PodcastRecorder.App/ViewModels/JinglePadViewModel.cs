using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App.ViewModels;

public partial class JingleCellViewModel(string key, int index, JinglePadViewModel pad) : ObservableObject
{
    public int Index { get; } = index;

    public string Key { get; } = key;

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

/// <summary>One tab of the jingle cart, e.g. one per show, with its own grid size.</summary>
public partial class JingleBankViewModel : ObservableObject
{
    private readonly int _id;
    private readonly JinglePadViewModel _pad;

    public JingleBankViewModel(int id, string name, int rows, int columns, JinglePadViewModel pad)
    {
        _id = id;
        _pad = pad;
        Name = name;
        Rows = Math.Clamp(rows, JinglePadViewModel.MinSize, JinglePadViewModel.MaxSize);
        Columns = Math.Clamp(columns, JinglePadViewModel.MinSize, JinglePadViewModel.MaxSize);
    }

    public ObservableCollection<JingleCellViewModel> Cells { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial int Rows { get; set; }

    [ObservableProperty]
    public partial int Columns { get; set; }

    partial void OnRowsChanged(int value) => Resize();

    partial void OnColumnsChanged(int value) => Resize();

    /// <summary>Grows or shrinks the grid; pads keep their file by position.</summary>
    private void Resize()
    {
        int size = Rows * Columns;
        while (Cells.Count > size)
        {
            var last = Cells[^1];
            _pad.Stop(last);
            Cells.RemoveAt(Cells.Count - 1);
        }

        while (Cells.Count < size)
        {
            int index = Cells.Count;
            Cells.Add(new JingleCellViewModel($"{_id}-{index}", index, _pad));
        }
    }
}

/// <summary>The jingle cart: tabs of pads, each pad holding one file.</summary>
public partial class JinglePadViewModel : ObservableObject
{
    public const int MinSize = 2;
    public const int MaxSize = 8;
    public const int DefaultSize = 4;

    private readonly JinglePlayer _player;
    private readonly IFileDialogs _dialogs;
    private int _nextBankId;

    public JinglePadViewModel(JinglePlayer player, IFileDialogs dialogs, SynchronizationContext ui)
    {
        _player = player;
        _dialogs = dialogs;
        _player.JingleEnded += (_, key) => ui.Post(_ => MarkStopped(key), null);
    }

    public static IReadOnlyList<int> SizeOptions { get; } = Enumerable.Range(MinSize, MaxSize - MinSize + 1).ToList();

    public ObservableCollection<JingleBankViewModel> Banks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveBankCommand))]
    public partial JingleBankViewModel? SelectedBank { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public JingleBankViewModel AddBank(string name, int rows = DefaultSize, int columns = DefaultSize)
    {
        var bank = new JingleBankViewModel(_nextBankId++, name, rows, columns, this);
        Banks.Add(bank);
        SelectedBank ??= bank;
        RemoveBankCommand.NotifyCanExecuteChanged();
        return bank;
    }

    [RelayCommand]
    private void NewBank() => SelectedBank = AddBank($"Sekme {Banks.Count + 1}");

    [RelayCommand(CanExecute = nameof(CanRemoveBank))]
    private void RemoveBank()
    {
        if (SelectedBank is not { } bank) return;
        foreach (var cell in bank.Cells) Stop(cell);
        int index = Banks.IndexOf(bank);
        Banks.Remove(bank);
        SelectedBank = Banks[Math.Min(index, Banks.Count - 1)];
        RemoveBankCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveBank() => SelectedBank is not null && Banks.Count > 1;

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
        foreach (var cell in Banks.SelectMany(b => b.Cells)) cell.IsPlaying = false;
    }

    internal string? PickFile() => _dialogs.PickAudioFiles(multiple: false).FirstOrDefault();

    private void MarkStopped(string key)
    {
        var cell = Banks.SelectMany(b => b.Cells).FirstOrDefault(c => c.Key == key);
        if (cell is not null && !_player.IsPlaying(key)) cell.IsPlaying = false;
    }
}
