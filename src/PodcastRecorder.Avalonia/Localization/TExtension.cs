namespace PodcastRecorder.App.Localization;

/// <summary>
/// <c>{loc:T Key}</c> in XAML: the text for the key in the current language, as in the WPF app.
/// The window is rebuilt when the language changes, so a plain string is enough. Prefix and Suffix
/// add fixed characters such as a symbol or a space.
/// </summary>
public sealed class TExtension(string key)
{
    public string Key { get; set; } = key;

    public string Prefix { get; set; } = "";

    public string Suffix { get; set; } = "";

    public string ProvideValue(IServiceProvider serviceProvider) => Prefix + Loc.T(Key) + Suffix;
}
