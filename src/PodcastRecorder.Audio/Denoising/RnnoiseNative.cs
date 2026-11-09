using System.Runtime.InteropServices;

namespace PodcastRecorder.Audio.Denoising;

/// <summary>
/// The RNNoise C library (Xiph/Mozilla, BSD). The native builds for Windows and Linux come from
/// the YellowDogMan.RRNoise.NET package; on a system without one the feature reports itself as
/// unavailable instead of failing.
/// </summary>
internal static partial class RnnoiseNative
{
    private const string Library = "rnnoise";

    /// <summary>RNNoise works on 10 ms frames of 48 kHz mono audio.</summary>
    public const int FrameSize = 480;

    /// <summary>RNNoise expects samples on the 16-bit scale, not -1..1.</summary>
    public const float Scale = 32768f;

    private static readonly Lazy<bool> Loaded = new(Probe);

    public static bool IsAvailable => Loaded.Value;

    [LibraryImport(Library)]
    public static partial nint rnnoise_create(nint model);

    [LibraryImport(Library)]
    public static partial void rnnoise_destroy(nint state);

    [LibraryImport(Library)]
    public static unsafe partial float rnnoise_process_frame(nint state, float* output, float* input);

    [LibraryImport(Library)]
    public static partial int rnnoise_get_frame_size();

    private static bool Probe()
    {
        try
        {
            return rnnoise_get_frame_size() == FrameSize;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }
}
