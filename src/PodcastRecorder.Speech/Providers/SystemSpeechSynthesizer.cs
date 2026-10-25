using System.Diagnostics;
using System.Runtime.Versioning;

namespace PodcastRecorder.Speech.Providers;

/// <summary>
/// The offline voices of Linux and macOS: espeak-ng on Linux (Turkish voice "tr" included) and
/// the "say" command on macOS (Turkish voice "Yelda"). The Windows counterpart is
/// <see cref="WindowsSpeechSynthesizer"/>.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class SystemSpeechSynthesizer : ITextToSpeech
{
    public string FileExtension => ".wav";

    public int MaxCharacters => 20000;

    private static string Command => OperatingSystem.IsMacOS() ? "say" : "espeak-ng";

    public static IReadOnlyList<string> InstalledVoices()
    {
        var output = Run(OperatingSystem.IsMacOS() ? ["-v", "?"] : ["--voices"], CancellationToken.None).GetAwaiter().GetResult();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // say:       "Yelda               tr_TR    # Merhaba, benim adım Yelda."
        // espeak-ng: "Pty Language       Age/Gender VoiceName          File                 Other Languages"
        //            " 5  tr              --/M      Turkish            trk/tr"
        return OperatingSystem.IsMacOS()
            ? lines.Select(l => l.Split('#')[0].Trim()).Select(l => l[..Math.Max(0, l.LastIndexOf(' '))].Trim()).Where(n => n.Length > 0).ToList()
            : lines.Skip(1).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length > 1).Select(p => p[1]).ToList();
    }

    public async Task SynthesizeAsync(string text, string voice, string outputPath, CancellationToken cancellationToken)
    {
        var textFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(textFile, text, cancellationToken).ConfigureAwait(false);
            List<string> arguments = OperatingSystem.IsMacOS()
                ? ["-f", textFile, "-o", outputPath, "--file-format=WAVE", "--data-format=LEI16@48000"]
                : ["-f", textFile, "-w", outputPath];
            if (!string.IsNullOrWhiteSpace(voice)) arguments.InsertRange(0, ["-v", voice.Trim()]);
            await Run(arguments, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(textFile);
        }
    }

    private static async Task<string> Run(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new SpeechException(OperatingSystem.IsMacOS()
                ? Texts.Get("System_SayFailed")
                : Texts.Get("System_EspeakMissing"), ex);
        }

        using (process)
        using (cancellationToken.Register(() => process.Kill()))
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new SpeechException($"{Command} hata verdi: {(await errors.ConfigureAwait(false)).Trim()}");
            return await output.ConfigureAwait(false);
        }
    }
}
