using System.Globalization;
using System.Runtime.CompilerServices;

namespace PodcastRecorder.Audio.Tests;

internal static class TestCulture
{
    /// <summary>
    /// The tests check the Turkish (neutral) messages, so they run in Turkish whatever the
    /// machine's language is; <see cref="LocalizationTests"/> switches to English where needed.
    /// </summary>
    [ModuleInitializer]
    internal static void UseTurkish()
    {
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentCulture = turkish;
        CultureInfo.DefaultThreadCurrentUICulture = turkish;
        CultureInfo.CurrentCulture = turkish;
        CultureInfo.CurrentUICulture = turkish;
    }
}
