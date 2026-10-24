using System.Windows;

namespace PodcastRecorder.App.Services;

public interface ILiveSettingsDialog
{
    /// <summary>Shows the settings window; returns true when the user saved changes into <paramref name="settings"/>.</summary>
    bool Edit(LiveStreamSettings settings);
}

public sealed class LiveSettingsDialog : ILiveSettingsDialog
{
    public bool Edit(LiveStreamSettings settings) =>
        new LiveSettingsWindow(settings) { Owner = Application.Current.MainWindow }.ShowDialog() == true;
}
