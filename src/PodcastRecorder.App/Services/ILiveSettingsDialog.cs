namespace PodcastRecorder.App.Services;

public interface ILiveSettingsDialog
{
    /// <summary>Shows the settings window; returns true when the user saved changes into <paramref name="settings"/>.</summary>
    bool Edit(LiveStreamSettings settings);
}
