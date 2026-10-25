using Avalonia.Controls;
using PodcastRecorder.App.Views;

namespace PodcastRecorder.App.Services;

public sealed class LiveSettingsDialog(App app) : ILiveSettingsDialog
{
    public bool Edit(LiveStreamSettings settings) =>
        Modal.Wait(new LiveSettingsWindow(settings).ShowDialog<bool>(app.MainWindow!));
}
