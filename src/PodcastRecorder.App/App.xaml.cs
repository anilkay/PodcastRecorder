using System.Windows;
using PodcastRecorder.App.Services;
using PodcastRecorder.App.ViewModels;

namespace PodcastRecorder.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _viewModel = new MainViewModel(new SettingsStore(), new FileDialogs());
        var window = new MainWindow { DataContext = _viewModel };
        window.Show();
        _viewModel.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        base.OnExit(e);
    }
}
