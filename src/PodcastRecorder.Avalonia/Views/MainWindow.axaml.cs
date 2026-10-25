using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PodcastRecorder.App.Localization;
using PodcastRecorder.App.ViewModels;
using PodcastRecorder.Audio.Sources;

namespace PodcastRecorder.App.Views;

/// <summary>
/// Code-behind handles what bindings cannot: drag and drop, double clicks, the jingle pad keys
/// and the small help and file info windows.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Files dragged from the folder browser, inside the app.</summary>
    private static readonly DataFormat<string> BrowserFile = DataFormat.CreateInProcessFormat<string>("PodcastRecorder.BrowserFile");

    /// <summary>Keys held down, so auto-repeat does not toggle a jingle again and again.</summary>
    private readonly HashSet<Key> _heldKeys = [];
    private PointerPressedEventArgs? _dragStart;
    private BrowserEntry? _dragEntry;

    /// <summary>The logo in the help header (the window icon is the same picture).</summary>
    private static readonly Lazy<Bitmap> LogoBitmap =
        new(() => new Bitmap(AssetLoader.Open(new Uri("avares://PodcastRecorder.Avalonia/Assets/logo.png"))));

    public MainWindow()
    {
        InitializeComponent();
        SystemVoicesTab.Header = Loc.T(OperatingSystem.IsWindows() ? "Main_WindowsSesleri" : "Cross_SystemVoices");
        SystemVoicesHelp.Text = Loc.T(OperatingSystem.IsWindows() ? "Main_InternetsizVeUcretsizdirTurkceSes"
            : OperatingSystem.IsMacOS() ? "Cross_SystemVoicesMac" : "Cross_SystemVoicesLinux");

        AddHandler(KeyDownEvent, JingleKey_KeyDown, RoutingStrategies.Bubble);
        AddHandler(KeyUpEvent, (_, e) => _heldKeys.Remove(e.Key), RoutingStrategies.Tunnel);
        // A key released in another window never reaches this one; without this the next press
        // would look like auto-repeat and be ignored.
        Deactivated += (_, _) => _heldKeys.Clear();
        AddHandler(DragDrop.DragOverEvent, FileDrop_DragOver);
        AddHandler(DragDrop.DropEvent, FileDrop_Drop);
        BrowserList.AddHandler(PointerPressedEvent, BrowserList_PointerPressed, RoutingStrategies.Tunnel);
        BrowserList.AddHandler(PointerMovedEvent, BrowserList_PointerMoved, RoutingStrategies.Tunnel);
        ServiceSettings.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty) ScrollToServiceSettings();
        };
        ServiceTabs.SelectionChanged += (_, e) =>
        {
            // SelectionChanged also bubbles up from the combo boxes inside the tabs.
            if (ReferenceEquals(e.Source, ServiceTabs)) ScrollToServiceSettings();
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// Plays the pad whose key was pressed. Handled here because a key binding needs a modifier
    /// for plain letter and digit keys to be useful next to text boxes.
    /// </summary>
    private void JingleKey_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            ShowHelp();
            e.Handled = true;
            return;
        }

        bool repeat = !_heldKeys.Add(e.Key);
        if (e.Handled || repeat || e.KeyModifiers != KeyModifiers.None
            || e.Source is TextBox || FocusManager?.GetFocusedElement() is TextBox || ViewModel is not { } vm)
        {
            return;
        }

        char? key = e.Key switch
        {
            >= Key.D0 and <= Key.D9 => (char)('0' + (e.Key - Key.D0)),
            >= Key.A and <= Key.Z => (char)('A' + (e.Key - Key.A)),
            _ => null,
        };
        if (key is { } k && vm.Jingles.CellForShortcut(k) is { } cell)
        {
            cell.ToggleCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ServiceSettingsButton_Click(object? sender, RoutedEventArgs e) => ScrollToServiceSettings();

    /// <summary>
    /// Scrolls "Servis ayarları" into view when it opens or its tab changes (a ⚙ Ayarlar button or a
    /// missing key), since it sits at the bottom of the AI panel.
    /// </summary>
    private void ScrollToServiceSettings() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (ServiceSettings.IsExpanded) ServiceSettings.BringIntoView();
        }, DispatcherPriority.Background);

    private void RecordingTags_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) _ = new RecordingTagsWindow { DataContext = vm.Tags }.ShowDialog(this);
    }

    private void Help_Click(object? sender, RoutedEventArgs e) => ShowHelp();

    /// <summary>Shows the shortcut list in a monospace font so the key column lines up.</summary>
    private void ShowHelp()
    {
        var ok = new Button
        {
            Content = Loc.T("Help_Ok"),
            IsDefault = true,
            IsCancel = true,
            MinWidth = 90,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        // Header: the logo with the app name and version (the main window's title).
        var header = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14),
            Children =
            {
                new Image { Source = LogoBitmap.Value, Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) },
                new TextBlock
                {
                    Text = Title,
                    FontSize = 18,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            },
        };
        var help = new Window
        {
            Title = Loc.T("Help_Title"),
            Icon = Icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20, 16, 20, 16),
                Children =
                {
                    header,
                    new TextBlock { Text = Loc.T("Help_Shortcuts"), FontFamily = (Avalonia.Media.FontFamily)this.FindResource("MonoFont")!, FontSize = 13 },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => help.Close();
        _ = help.ShowDialog(this);
    }

    private void BrowserList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel?.Browser.OpenCommand is { } open && open.CanExecute(null)) open.Execute(null);
    }

    private void PlaylistList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel?.Playlist.PlayCommand is { } play && play.CanExecute(null)) play.Execute(null);
    }

    private void BrowserList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetCurrentPoint(BrowserList).Properties.IsLeftButtonPressed ? e : null;
        _dragEntry = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as BrowserEntry;
    }

    private async void BrowserList_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || _dragEntry is not { IsFolder: false } entry
            || !e.GetCurrentPoint(BrowserList).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var delta = e.GetPosition(BrowserList) - start.GetPosition(BrowserList);
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

        _dragStart = null;
        _dragEntry = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(BrowserFile, entry.FullPath));
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy);
    }

    private void FileDrop_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DropTarget(e) is not null && DroppedAudioFiles(e).Any() ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Files dropped on the playlist join it; a file dropped on a jingle pad is assigned to it.</summary>
    private void FileDrop_Drop(object? sender, DragEventArgs e)
    {
        var files = DroppedAudioFiles(e).ToList();
        switch (DropTarget(e))
        {
            case JingleCellViewModel cell when files.Count > 0:
                cell.Assign(files[0]);
                break;
            case PlaylistViewModel playlist:
                playlist.AddFiles(files);
                break;
        }

        e.Handled = true;
    }

    private object? DropTarget(DragEventArgs e)
    {
        for (var visual = e.Source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button { DataContext: JingleCellViewModel cell }) return cell;
            if (ReferenceEquals(visual, PlaylistList)) return ViewModel?.Playlist;
        }

        return null;
    }

    private static IEnumerable<string> DroppedAudioFiles(DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetValues(BrowserFile) is { } own
            ? own
            : (e.DataTransfer.TryGetFiles() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>();
        return paths.Where(AudioFiles.IsSupported);
    }
}
