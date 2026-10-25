using System.IO;
using Microsoft.Win32;
using PodcastRecorder.Audio.Sources;
using PodcastRecorder.App.Localization;

namespace PodcastRecorder.App.Services;

public sealed class FileDialogs : IFileDialogs
{
    private static string AudioFilter =>
        Loc.T("Dialog_AudioFiles") + "|" + string.Join(';', AudioFiles.SupportedExtensions.Select(e => "*" + e)) + "|" + Loc.T("Dialog_AllFiles") + "|*.*";

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

    public string? PickImage()
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("Dialog_Images") + "|*.jpg;*.jpeg;*.png" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
