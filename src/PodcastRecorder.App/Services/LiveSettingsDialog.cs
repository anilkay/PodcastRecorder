using System.Windows;

namespace PodcastRecorder.App.Services;

public sealed class LiveSettingsDialog : ILiveSettingsDialog
{
    public bool Edit(LiveStreamSettings settings) =>
        new LiveSettingsWindow(settings) { Owner = Application.Current.MainWindow }.ShowDialog() == true;
}
