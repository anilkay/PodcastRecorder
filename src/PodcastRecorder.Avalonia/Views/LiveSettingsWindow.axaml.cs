using PodcastRecorder.App.Localization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Streaming;

namespace PodcastRecorder.App.Views;

/// <summary>
/// Edits <see cref="LiveStreamSettings"/>. The password goes straight from the box into
/// <see cref="Secrets"/>, as in the WPF app.
/// </summary>
public partial class LiveSettingsWindow : Window
{
    private readonly LiveStreamSettings _settings;

    /// <summary>For the XAML previewer.</summary>
    public LiveSettingsWindow()
        : this(new LiveStreamSettings())
    {
    }

    public LiveSettingsWindow(LiveStreamSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        HostBox.Text = settings.Host;
        PortBox.Text = settings.Port.ToString();
        MountBox.Text = settings.Mount;
        UserBox.Text = settings.User;
        PasswordBox.Text = Secrets.Unprotect(settings.ProtectedPassword) ?? "";
        BitrateBox.ItemsSource = IcecastSettings.Bitrates.Select(b => $"MP3 {b} kbps").ToList();
        BitrateBox.SelectedIndex = Math.Max(0, IcecastSettings.Bitrates.ToList().IndexOf(settings.BitrateKbps));
        NameBox.Text = settings.StreamName;
        Opened += (_, _) => HostBox.Focus();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse((PortBox.Text ?? "").Trim(), out int port))
        {
            ErrorText.Text = Loc.T("LiveSettings_PortNumber");
            return;
        }

        var candidate = new IcecastSettings(
            (HostBox.Text ?? "").Trim(),
            port,
            (MountBox.Text ?? "").Trim(),
            (UserBox.Text ?? "").Trim(),
            PasswordBox.Text ?? "",
            IcecastSettings.Bitrates[Math.Max(0, BitrateBox.SelectedIndex)],
            (NameBox.Text ?? "").Trim());
        if (candidate.Validate() is { } problem)
        {
            ErrorText.Text = problem;
            return;
        }

        _settings.Host = candidate.Host;
        _settings.Port = candidate.Port;
        _settings.Mount = candidate.Mount;
        _settings.User = candidate.User;
        _settings.ProtectedPassword = Secrets.Protect(candidate.Password);
        _settings.BitrateKbps = candidate.BitrateKbps;
        _settings.StreamName = candidate.StreamName ?? "";
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
