using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PodcastRecorder.App.ViewModels;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App;

/// <summary>
/// Code-behind only handles drag and drop, which WPF does not expose to bindings, and the
/// jingle pad keys, which depend on the pads in the view model.
/// </summary>
public partial class MainWindow : Window
{
    private const string ShortcutHelp =
        """
        Kayıt
          Ctrl+R          Kaydı başlat
          Ctrl+P          Duraklat / devam
          Ctrl+Shift+R    Kaydı durdur ve kaydet
          Ctrl+M          Mikrofonu kapat / aç

        Canlı yayın
          Ctrl+L          Yayına başla
          Ctrl+Shift+L    Yayını durdur

        Çalma listesi
          F5              Seçili şarkıyı çal
          F6              Müziği durdur
          F7              Sonraki şarkı

        Jingle paneli (tuşlar paneldeki yerleriyle aynı)
          1 2 3 4
          Q W E R
          A S D F
          Z X C V         Jingle çal / durdur
          Esc             Bütün jingle'ları durdur
        """;

    private Point _dragStart;
    private BrowserEntry? _dragEntry;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += JingleKey_KeyDown;
    }

    /// <summary>
    /// Plays the pad whose key was pressed. Handled here because <see cref="KeyBinding"/> does not
    /// accept plain letter and digit keys without a modifier.
    /// </summary>
    private void JingleKey_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None
            || e.OriginalSource is TextBoxBase || DataContext is not MainViewModel vm)
        {
            return;
        }

        char? key = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => (char)('0' + (e.Key - Key.D0)),
            >= Key.A and <= Key.Z => (char)('A' + (e.Key - Key.A)),
            _ => null,
        };
        int index = key is { } k ? JinglePadViewModel.ShortcutKeys.IndexOf(k) : -1;
        if (index >= 0 && index < vm.Jingles.Cells.Count)
        {
            vm.Jingles.Cells[index].ToggleCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Help_Executed(object sender, ExecutedRoutedEventArgs e) =>
        MessageBox.Show(this, ShortcutHelp, "Klavye kısayolları", MessageBoxButton.OK, MessageBoxImage.Information);

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
