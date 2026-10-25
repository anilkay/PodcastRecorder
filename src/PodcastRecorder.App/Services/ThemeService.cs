using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using PodcastRecorder.App.Localization;

namespace PodcastRecorder.App.Services;

public enum AppTheme
{
    /// <summary>Follows Windows' "app mode" (Settings › Personalisation › Colours).</summary>
    System,
    Light,
    Dark,
}

/// <summary>A theme choice; <see cref="Name"/> is in the current language.</summary>
public sealed record ThemeOption(AppTheme Theme, string NameKey)
{
    public string Name => Loc.T(NameKey);
}

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
        new(AppTheme.System, "Theme_System"),
        new(AppTheme.Light, "Theme_Light"),
        new(AppTheme.Dark, "Theme_Dark"),
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
        // merged[2], Themes/Controls.xaml, stays: its buttons take their colours from merged[1].

        foreach (Window window in app.Windows)
        {
            ApplyTitleBar(window);
        }
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
