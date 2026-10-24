using System.IO;
using Microsoft.Win32;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App.Services;

public interface IFileDialogs
{
    IReadOnlyList<string> PickAudioFiles(bool multiple);

    string? PickFolder(string? initialFolder);
}

public sealed class FileDialogs : IFileDialogs
{
    private static readonly string AudioFilter =
        "Ses dosyaları|" + string.Join(';', AudioFiles.SupportedExtensions.Select(e => "*" + e)) + "|Tüm dosyalar|*.*";

    public IReadOnlyList<string> PickAudioFiles(bool multiple)
    {
        var dialog = new OpenFileDialog { Filter = AudioFilter, Multiselect = multiple };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? PickFolder(string? initialFolder)
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
