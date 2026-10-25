using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PodcastRecorder.Audio.Recording;
using PodcastRecorder.Audio.Streaming;

namespace PodcastRecorder.Audio.Tests;

public partial class LocalizationTests
{
    /// <summary>Every project with texts: the Turkish Strings.resx and its English twin.</summary>
    public static TheoryData<string> ResourceFolders => new()
    {
        "src/PodcastRecorder.App/Localization",
        "src/PodcastRecorder.Audio",
        "src/PodcastRecorder.Speech",
    };

    [Theory]
    [MemberData(nameof(ResourceFolders))]
    public void English_has_every_turkish_text_with_the_same_placeholders(string folder)
    {
        var turkish = Read(Path.Combine(RepositoryRoot(), folder, "Strings.resx"));
        var english = Read(Path.Combine(RepositoryRoot(), folder, "Strings.en.resx"));

        Assert.NotEmpty(turkish);
        Assert.Equal(turkish.Keys.Order(), english.Keys.Order());
        foreach (var (key, text) in turkish)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} is empty in Turkish");
            Assert.False(string.IsNullOrWhiteSpace(english[key]), $"{key} is empty in English");
            Assert.True(Placeholders(text).SetEquals(Placeholders(english[key])),
                $"{key}: placeholders differ ({text} / {english[key]})");
        }
    }

    [Fact]
    public void Library_messages_follow_the_ui_culture()
    {
        var settings = new IcecastSettings("", 8000, "/canli", "source", "gizli", 128, null);
        Assert.Equal("Sunucu adresi gerekli.", settings.Validate());

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("A server address is required.", settings.Validate());
            Assert.Equal("FLAC (lossless)", EncoderCatalog.Find("flac").DisplayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        Assert.Equal("FLAC (kayıpsız)", EncoderCatalog.Find("flac").DisplayName);
    }

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    /// <summary>The indexes used in {0}, {1:0.0} and the like.</summary>
    private static HashSet<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static string RepositoryRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
