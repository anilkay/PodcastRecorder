namespace PodcastRecorder.Speech;

/// <summary>A voice synthesis service that writes an audio file.</summary>
public interface ITextToSpeech
{
    /// <summary>Extension of the file it writes, with the dot.</summary>
    string FileExtension { get; }

    int MaxCharacters { get; }

    Task SynthesizeAsync(string text, string voice, string outputPath, CancellationToken cancellationToken);
}
