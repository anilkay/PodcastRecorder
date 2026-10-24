using System.Windows;
using PodcastRecorder.App.Services;
using PodcastRecorder.Audio.Streaming;

namespace PodcastRecorder.App;

/// <summary>
/// Edits <see cref="LiveStreamSettings"/>. Code-behind because <see cref="System.Windows.Controls.PasswordBox"/>
/// cannot be bound; the password goes straight from the box into DPAPI.
/// </summary>
public partial class LiveSettingsWindow : Window
{
    private readonly LiveStreamSettings _settings;

    public LiveSettingsWindow(LiveStreamSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        HostBox.Text = settings.Host;
        PortBox.Text = settings.Port.ToString();
        MountBox.Text = settings.Mount;
        UserBox.Text = settings.User;
        PasswordBox.Password = Secrets.Unprotect(settings.ProtectedPassword) ?? "";
        BitrateBox.ItemsSource = IcecastSettings.Bitrates.Select(b => $"MP3 {b} kbps").ToList();
        BitrateBox.SelectedIndex = Math.Max(0, IcecastSettings.Bitrates.ToList().IndexOf(settings.BitrateKbps));
        NameBox.Text = settings.StreamName;
        Loaded += (_, _) => HostBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out int port))
        {
            ErrorText.Text = "Port bir sayı olmalı.";
            return;
        }

        var candidate = new IcecastSettings(
            HostBox.Text.Trim(),
            port,
            MountBox.Text.Trim(),
            UserBox.Text.Trim(),
            PasswordBox.Password,
            IcecastSettings.Bitrates[BitrateBox.SelectedIndex],
            NameBox.Text.Trim());
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
        DialogResult = true;
    }
}
