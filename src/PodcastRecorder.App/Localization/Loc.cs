using System.Globalization;
using System.Resources;

namespace PodcastRecorder.App.Localization;

/// <summary>A language the user can pick; <see cref="Name"/> is written in that language.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// The app's texts: Strings.resx is Turkish (the neutral language), Strings.en.resx English.
/// <see cref="Apply"/> switches the UI culture; the libraries' own messages follow it too.
/// </summary>
public static class Loc
{
    public const string System = "system";

    private static readonly ResourceManager Resources =
        new("PodcastRecorder.App.Localization.Strings", typeof(Loc).Assembly);

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(System, "Sistem / System"),
        new("tr", "Türkçe"),
        new("en", "English"),
    ];

    /// <summary>The culture in use, e.g. tr-TR or en-US.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>The text for <paramref name="key"/> in the current language (the key itself if it is missing).</summary>
    public static string T(string key) => Resources.GetString(key, Culture) ?? key;

    /// <summary>A text with {0}-style placeholders filled in, formatted for the current language.</summary>
    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>"tr" or "en" for a saved setting ("system" follows the Windows display language).</summary>
    public static string Resolve(string? setting) => setting switch
    {
        "tr" or "en" => setting,
        _ => CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en",
    };

    /// <summary>Sets the UI and formatting culture of every thread to the language of <paramref name="setting"/>.</summary>
    public static void Apply(string? setting)
    {
        Culture = CultureInfo.GetCultureInfo(Resolve(setting) == "tr" ? "tr-TR" : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }
}
