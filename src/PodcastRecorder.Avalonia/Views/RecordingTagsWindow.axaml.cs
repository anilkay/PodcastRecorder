using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace PodcastRecorder.App.Views;

/// <summary>"Dosya bilgileri": edits the main view model's <see cref="ViewModels.RecordingTagsViewModel"/> directly.</summary>
public partial class RecordingTagsWindow : Window
{
    public RecordingTagsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}

/// <summary>
/// Loads a picture file for the cover preview without keeping it open, so the file can still be
/// replaced or deleted while the window shows it.
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return Bitmap.DecodeToWidth(stream, 300);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
