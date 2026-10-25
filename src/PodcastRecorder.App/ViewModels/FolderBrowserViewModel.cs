using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Sources;
using PodcastRecorder.App.Localization;

namespace PodcastRecorder.App.ViewModels;

public sealed record BrowserEntry(string Name, string FullPath, bool IsFolder)
{
    public string Icon => IsFolder ? "📁" : "🎵";
}

/// <summary>Lists sub-folders and playable files of one folder at a time.</summary>
public partial class FolderBrowserViewModel(IFileDialogs dialogs, Action<string> addToPlaylist) : ObservableObject
{
    public ObservableCollection<BrowserEntry> Entries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    public partial string? CurrentFolder { get; set; }

    [ObservableProperty]
    public partial BrowserEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public void Navigate(string? folder)
    {
        Entries.Clear();
        CurrentFolder = folder;
        ErrorMessage = null;
        if (folder is null || !Directory.Exists(folder)) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder).Order(StringComparer.CurrentCultureIgnoreCase))
            {
                var info = new DirectoryInfo(dir);
                if (info.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                Entries.Add(new BrowserEntry(info.Name, dir, IsFolder: true));
            }

            foreach (var file in Directory.EnumerateFiles(folder).Where(AudioFiles.IsSupported).Order(StringComparer.CurrentCultureIgnoreCase))
            {
                Entries.Add(new BrowserEntry(Path.GetFileName(file), file, IsFolder: false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = Loc.F("Browser_ReadFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        if (dialogs.PickFolder(CurrentFolder) is { } folder) Navigate(folder);
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void Up() => Navigate(Directory.GetParent(CurrentFolder!)?.FullName);

    [RelayCommand]
    private void Open(BrowserEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null) return;
        if (entry.IsFolder) Navigate(entry.FullPath);
        else addToPlaylist(entry.FullPath);
    }

    private bool CanGoUp() => CurrentFolder is not null && Directory.GetParent(CurrentFolder) is not null;
}
