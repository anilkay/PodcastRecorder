using System.Windows;
using System.Windows.Controls;

namespace PodcastRecorder.App.Services;

/// <summary>
/// Lets a PasswordBox bind its password (<c>services:PasswordBoxAssist.Password="{Binding Key}"</c>),
/// which WPF does not allow directly.
/// </summary>
public static class PasswordBoxAssist
{
    // Defaults to null so that binding an empty string still attaches the handler.
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxAssist),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBoxAssist));

    public static string? GetPassword(DependencyObject element) => (string?)element.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject element, string? value) => element.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        box.PasswordChanged -= Box_PasswordChanged;
        if (!(bool)box.GetValue(IsUpdatingProperty))
        {
            box.Password = (string?)e.NewValue ?? "";
        }

        box.PasswordChanged += Box_PasswordChanged;
    }

    private static void Box_PasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
