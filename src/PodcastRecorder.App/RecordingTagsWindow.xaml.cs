using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace PodcastRecorder.App;

/// <summary>"Dosya bilgileri": edits the main view model's <see cref="ViewModels.RecordingTagsViewModel"/> directly.</summary>
public partial class RecordingTagsWindow : Window
{
    public RecordingTagsWindow()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Loads a picture file for the cover preview without keeping it open, so the file can still be
/// replaced or deleted while the window shows it.
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 300;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
