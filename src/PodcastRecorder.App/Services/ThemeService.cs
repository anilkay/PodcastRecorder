using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;

namespace PodcastRecorder.App.Services;

public enum AppTheme
{
    /// <summary>Follows Windows' "app mode" (Settings › Personalisation › Colours).</summary>
    System,
    Light,
    Dark,
}

public sealed record ThemeOption(AppTheme Theme, string Name);

/// <summary>
/// Switches between light and dark: the WPF Fluent control theme plus the app's own colours
/// (Themes/Light.xaml, Themes/Dark.xaml), which the views use through DynamicResource.
/// </summary>
public static class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;

    private static AppTheme _theme;
    private static bool _isDark;
    private static bool _hooked;

    public static IReadOnlyList<ThemeOption> Options { get; } =
    [
        new(AppTheme.System, "Sistem"),
        new(AppTheme.Light, "Açık"),
        new(AppTheme.Dark, "Koyu"),
    ];

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        Hook();
        _theme = theme;
        _isDark = theme == AppTheme.Dark || (theme == AppTheme.System && WindowsUsesDarkMode());
        var variant = _isDark ? "Dark" : "Light";
        var merged = app.Resources.MergedDictionaries;
        merged[0] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{variant}.xaml"),
        };
        merged[1] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{variant}.xaml") };
        ApplyButtonStyles(app.Resources, (Style)merged[0][typeof(Button)]);

        foreach (Window window in app.Windows)
        {
            ApplyTitleBar(window);
        }
    }

    /// <summary>
    /// The app's compact button spacing on top of the Fluent button, rebuilt for each theme so every
    /// button keeps the Fluent template. Views use "TransportButton" through DynamicResource.
    /// </summary>
    private static void ApplyButtonStyles(ResourceDictionary resources, Style fluentButton)
    {
        var button = new Style(typeof(Button), fluentButton)
        {
            Setters =
            {
                new Setter(Control.PaddingProperty, new Thickness(10, 4, 10, 4)),
                new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)),
                new Setter(FrameworkElement.MinWidthProperty, 32.0),
            },
        };
        resources[typeof(Button)] = button;
        resources["TransportButton"] = new Style(typeof(Button), button)
        {
            Setters =
            {
                new Setter(Control.FontSizeProperty, 14.0),
                new Setter(Control.PaddingProperty, new Thickness(14, 6, 14, 6)),
                new Setter(FrameworkElement.MinWidthProperty, 96.0),
            },
        };
    }

    private static void Hook()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyTitleBar((Window)sender)));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (_theme == AppTheme.System && e.Category == UserPreferenceCategory.General)
            {
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
            }
        };
    }

    private static bool WindowsUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>Dark title bar to match (Windows 10 20H1 and later; ignored elsewhere).</summary>
    private static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int dark = _isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
