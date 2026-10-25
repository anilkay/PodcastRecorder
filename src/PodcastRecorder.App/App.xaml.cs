using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using PodcastRecorder.App.Localization;
using PodcastRecorder.App.Services;
using PodcastRecorder.App.ViewModels;

namespace PodcastRecorder.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsStore = new SettingsStore();
        // The language must be set before any view model or window reads a text.
        Loc.Apply(settingsStore.Load().Language);
        _viewModel = new MainViewModel(settingsStore, new FileDialogs(), new LiveSettingsDialog());
        _viewModel.LanguageChanged += (_, _) => RebuildMainWindow();
        CreateMainWindow().Show();
        _viewModel.Initialize();
    }

    private MainWindow CreateMainWindow()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return new MainWindow
        {
            DataContext = _viewModel,
            Title = $"Podcast Recorder {version}",
            // Bindings format numbers and dates for the chosen language.
            Language = XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag),
        };
    }

    /// <summary>
    /// Switches the language by building the main window again over the same view model, so every
    /// text (including list items and computed texts) is read in the new language. Recording,
    /// playback and the live stream carry on.
    /// </summary>
    private void RebuildMainWindow()
    {
        var old = MainWindow;
        var window = CreateMainWindow();
        if (old is not null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = old.Left;
            window.Top = old.Top;
            window.Width = old.Width;
            window.Height = old.Height;
        }

        window.Show();
        if (old is not null)
        {
            window.WindowState = old.WindowState;
        }

        MainWindow = window;
        old?.Close();
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        base.OnExit(e);
    }
}
