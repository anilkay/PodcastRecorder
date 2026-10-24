using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PodcastRecorder.App.ViewModels;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App;

/// <summary>
/// Code-behind only handles drag and drop, which WPF does not expose to bindings.
/// </summary>
public partial class MainWindow : Window
{
    private Point _dragStart;
    private BrowserEntry? _dragEntry;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void BrowserList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragEntry = ItemsControl.ContainerFromElement(BrowserList, (DependencyObject)e.OriginalSource) is ListBoxItem item
            ? item.DataContext as BrowserEntry
            : null;
    }

    private void BrowserList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragEntry is not { IsFolder: false } entry)
        {
            return;
        }

        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragEntry = null;
        DragDrop.DoDragDrop(BrowserList, new DataObject(DataFormats.FileDrop, new[] { entry.FullPath }), DragDropEffects.Copy);
    }

    private void FileDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedAudioFiles(e).Any() ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Playlist_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Playlist.AddFiles(DroppedAudioFiles(e));
        }
    }

    private void JingleCell_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JingleCellViewModel cell }
            && DroppedAudioFiles(e).FirstOrDefault() is { } path)
        {
            cell.Assign(path);
        }

        e.Handled = true;
    }

    private static IEnumerable<string> DroppedAudioFiles(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files ? files.Where(AudioFiles.IsSupported) : [];
}
