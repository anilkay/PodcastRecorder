using System.Globalization;
using System.Resources;

namespace PodcastRecorder.Audio;

/// <summary>
/// User-facing messages of this library: Strings.resx is Turkish, Strings.en.resx English. They
/// follow the calling thread's UI culture, which the app sets for every thread.
/// </summary>
internal static class Texts
{
    private static readonly ResourceManager Resources = new("PodcastRecorder.Audio.Strings", typeof(Texts).Assembly);

    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
