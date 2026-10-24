using System.Globalization;
using System.Text;

namespace PodcastRecorder.Speech;

/// <summary>One stretch of recognised speech, timed from the start of the recording.</summary>
public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

public sealed class Transcript(IReadOnlyList<TranscriptSegment> segments)
{
    public IReadOnlyList<TranscriptSegment> Segments { get; } = segments;

    /// <summary>One line per segment, each starting with its time, so a passage can be found in the audio.</summary>
    public string ToPlainText()
    {
        var text = new StringBuilder();
        foreach (var segment in Segments)
        {
            text.Append('[').Append(FormatClock(segment.Start)).Append("] ").AppendLine(segment.Text);
        }

        return text.ToString();
    }

    /// <summary>SubRip subtitles, accepted by video editors, YouTube and most podcast hosts.</summary>
    public string ToSrt()
    {
        var text = new StringBuilder();
        for (int i = 0; i < Segments.Count; i++)
        {
            var segment = Segments[i];
            text.Append(i + 1).Append('\n')
                .Append(FormatSrtTime(segment.Start)).Append(" --> ").Append(FormatSrtTime(segment.End)).Append('\n')
                .Append(segment.Text).Append("\n\n");
        }

        return text.ToString();
    }

    /// <summary>Writes <c>name.txt</c> and <c>name.srt</c> next to the audio file.</summary>
    /// <returns>The path of the text file.</returns>
    public async Task<string> SaveNextToAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        var basePath = Path.Combine(Path.GetDirectoryName(audioPath) ?? "", Path.GetFileNameWithoutExtension(audioPath));
        var textPath = basePath + ".txt";
        await File.WriteAllTextAsync(textPath, ToPlainText(), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(basePath + ".srt", ToSrt(), Encoding.UTF8, cancellationToken);
        return textPath;
    }

    private static string FormatClock(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);

    private static string FormatSrtTime(TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");
}
