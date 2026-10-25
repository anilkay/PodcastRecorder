using System.Runtime.Versioning;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;

namespace PodcastRecorder.Speech.Providers;

/// <summary>
/// The voices installed in Windows (Settings › Time and language › Speech). Offline and free;
/// Turkish needs the Turkish speech pack.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSpeechSynthesizer : ITextToSpeech
{
    public string FileExtension => ".wav";

    public int MaxCharacters => 20000;

    public static IReadOnlyList<string> InstalledVoices()
    {
        using var synthesizer = new SpeechSynthesizer();
        return synthesizer.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
    }

    public Task SynthesizeAsync(string text, string voice, string outputPath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var synthesizer = new SpeechSynthesizer();
            if (!string.IsNullOrWhiteSpace(voice))
            {
                try
                {
                    synthesizer.SelectVoice(voice);
                }
                catch (ArgumentException ex)
                {
                    throw new SpeechException(Texts.Format("Windows_VoiceMissing", voice), ex);
                }
            }

            synthesizer.SetOutputToWaveFile(outputPath, new SpeechAudioFormatInfo(48000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            using var registration = cancellationToken.Register(synthesizer.SpeakAsyncCancelAll);
            synthesizer.Speak(text);
            synthesizer.SetOutputToNull();
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);
}
