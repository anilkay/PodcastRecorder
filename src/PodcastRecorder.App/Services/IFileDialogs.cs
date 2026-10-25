namespace PodcastRecorder.App.Services;

/// <summary>Open dialogs the view models use; each UI (WPF, Avalonia) supplies its own.</summary>
public interface IFileDialogs
{
    IReadOnlyList<string> PickAudioFiles(bool multiple);

    string? PickFolder(string? initialFolder);

    /// <summary>A JPEG or PNG picture, for a recording's cover.</summary>
    string? PickImage();
}
