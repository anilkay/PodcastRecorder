using Avalonia;
using Avalonia.Styling;
using PodcastRecorder.App.Localization;

namespace PodcastRecorder.App.Services;

public enum AppTheme
{
    /// <summary>Follows the system's light or dark setting.</summary>
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
/// Switches between light and dark: Avalonia's Fluent theme variant, which also picks the app's
/// colours from the theme dictionaries in App.axaml. Same API as the WPF app's ThemeService.
/// </summary>
public static class ThemeService
{
    public static IReadOnlyList<ThemeOption> Options { get; } =
    [
        new(AppTheme.System, "Theme_System"),
        new(AppTheme.Light, "Theme_Light"),
        new(AppTheme.Dark, "Theme_Dark"),
    ];

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
