using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PodcastRecorder.App.Localization;
using PodcastRecorder.App.Services;
using PodcastRecorder.App.ViewModels;
using PodcastRecorder.App.Views;
using PodcastRecorder.Audio.Devices;
using PodcastRecorder.Audio.PortAudio;

namespace PodcastRecorder.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    private IClassicDesktopStyleApplicationLifetime? _desktop;

    /// <summary>The window dialogs open over; it changes when the language is switched.</summary>
    public Window? MainWindow => _desktop?.MainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private static MainWindow CreateMainWindow()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return new MainWindow { Title = $"Podcast Recorder {version}" };
    }

    /// <summary>
    /// Switches the language by building the main window again over the same view model, as the WPF
    /// app does. Recording, playback and the live stream carry on.
    /// </summary>
    private void RebuildMainWindow()
    {
        if (_desktop?.MainWindow is not { } old) return;
        var window = CreateMainWindow();
        window.DataContext = _viewModel;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = old.Position;
        window.Width = old.Width;
        window.Height = old.Height;
        window.WindowState = old.WindowState;
        _desktop.MainWindow = window;
        window.Show();
        old.Close();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            var settingsStore = new SettingsStore();
            // The language must be set before any view model or window reads a text.
            Loc.Apply(settingsStore.Load().Language);
            var window = CreateMainWindow();
            _viewModel = new MainViewModel(settingsStore, new FileDialogs(this), new LiveSettingsDialog(this), CreateAudioBackend());
            _viewModel.LanguageChanged += (_, _) => RebuildMainWindow();
            window.DataContext = _viewModel;
            desktop.MainWindow = window;
            window.Opened += (_, _) => _viewModel.Initialize();
            desktop.Exit += (_, _) => _viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>WASAPI on Windows, as in the WPF app; PortAudio (ALSA, PulseAudio, Core Audio) elsewhere.</summary>
    private static IAudioBackend CreateAudioBackend() =>
        OperatingSystem.IsWindows() ? new WasapiBackend() : new PortAudioBackend();
}
