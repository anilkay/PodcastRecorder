using PodcastRecorder.App.Localization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App.Services;

/// <summary>The system's open dialogs (GTK or the desktop portal on Linux, Cocoa on macOS, Win32 on Windows).</summary>
public sealed class FileDialogs(App app) : IFileDialogs
{
    private static FilePickerFileType AudioFileType => new(Loc.T("Dialog_AudioFiles"))
    {
        Patterns = Audio.Sources.AudioFiles.SupportedExtensions.Select(e => "*" + e).ToList(),
    };

    private static FilePickerFileType Images => new(Loc.T("Dialog_Images"))
    {
        Patterns = ["*.jpg", "*.jpeg", "*.png"],
        MimeTypes = ["image/jpeg", "image/png"],
    };

    /// <summary>Looked up per call: the main window is rebuilt when the language changes.</summary>
    private TopLevel Owner => app.MainWindow ?? throw new InvalidOperationException("No main window.");

    public IReadOnlyList<string> PickAudioFiles(bool multiple) =>
        Modal.Wait(PickFilesAsync(new FilePickerOpenOptions
        {
            AllowMultiple = multiple,
            FileTypeFilter = [AudioFileType, FilePickerFileTypes.All],
        }));

    public string? PickFolder(string? initialFolder) => Modal.Wait(PickFolderAsync(initialFolder));

    public string? PickImage() =>
        Modal.Wait(PickFilesAsync(new FilePickerOpenOptions { FileTypeFilter = [Images] })).FirstOrDefault();

    private async Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOpenOptions options)
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(options);
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private async Task<string?> PickFolderAsync(string? initialFolder)
    {
        var options = new FolderPickerOpenOptions();
        if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
        {
            options.SuggestedStartLocation = await Owner.StorageProvider.TryGetFolderFromPathAsync(initialFolder);
        }

        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }
}
