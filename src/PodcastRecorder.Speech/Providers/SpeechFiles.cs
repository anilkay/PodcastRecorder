namespace PodcastRecorder.Speech.Providers;

internal static class SpeechFiles
{
    /// <summary>Writes the body to a temporary file first, so a failed download leaves nothing behind.</summary>
    public static async Task WriteAsync(HttpResponseMessage response, string outputPath, CancellationToken cancellationToken)
    {
        var temp = outputPath + ".part";
        try
        {
            await using (var file = File.Create(temp))
            {
                await response.Content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temp, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
