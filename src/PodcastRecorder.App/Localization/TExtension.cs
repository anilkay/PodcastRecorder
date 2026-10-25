using System.Windows.Markup;

namespace PodcastRecorder.App.Localization;

/// <summary>
/// <c>{loc:T Key}</c> in XAML: the text for the key in the current language. Views are rebuilt when
/// the language changes, so a plain string is enough.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
