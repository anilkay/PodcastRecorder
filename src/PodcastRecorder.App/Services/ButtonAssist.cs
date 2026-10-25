using System.Windows;

namespace PodcastRecorder.App.Services;

/// <summary>
/// An icon glyph (Segoe Fluent Icons / Segoe MDL2 Assets) shown before a button's label by the
/// templates in Themes/Controls.xaml, e.g. <c>services:ButtonAssist.Icon="&amp;#xE768;"</c>.
/// </summary>
public static class ButtonAssist
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(ButtonAssist), new FrameworkPropertyMetadata(null));

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);
}
