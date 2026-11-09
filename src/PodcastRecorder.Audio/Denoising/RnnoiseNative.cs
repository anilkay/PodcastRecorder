using System.Runtime.InteropServices;

namespace PodcastRecorder.Audio.Denoising;

/// <summary>
/// The RNNoise C library (Xiph/Mozilla, BSD). The Windows and Linux builds come from the
/// YellowDogMan.RRNoise.NET package; the macOS build is compiled from the RNNoise release by
/// src/PodcastRecorder.Avalonia/macOS/build-rnnoise.sh and placed next to the app. When the library
/// is missing or does not work, the feature reports itself as unavailable instead of failing.
/// </summary>
internal static partial class RnnoiseNative
{
    private const string Library = "rnnoise";

    /// <summary>RNNoise works on 10 ms frames of 48 kHz mono audio.</summary>
    public const int FrameSize = 480;

    /// <summary>RNNoise expects samples on the 16-bit scale, not -1..1.</summary>
    public const float Scale = 32768f;

    private static readonly Lazy<(bool Works, int Delay)> Loaded = new(Probe);

    /// <summary>Whether the library loads and cleans a test signal.</summary>
    public static bool IsAvailable => Loaded.Value.Works;

    /// <summary>
    /// How far the library's output lags its input, in samples: 480 for the builds before 0.2,
    /// 960 for 0.2 (which looks one frame ahead). Measured once, so any build lines up.
    /// </summary>
    public static int ModelDelayFrames => Loaded.Value.Delay;

    [LibraryImport(Library)]
    public static partial nint rnnoise_create(nint model);

    [LibraryImport(Library)]
    public static partial void rnnoise_destroy(nint state);

    [LibraryImport(Library)]
    public static unsafe partial float rnnoise_process_frame(nint state, float* output, float* input);

    [LibraryImport(Library)]
    public static partial int rnnoise_get_frame_size();

    private static (bool, int) Probe()
    {
        try
        {
            if (rnnoise_get_frame_size() != FrameSize) return (false, 0);
            nint state = rnnoise_create(0);
            if (state == 0) return (false, 0);
            try
            {
                return MeasureDelay(state) is int delay ? (true, delay) : (false, 0);
            }
            finally
            {
                rnnoise_destroy(state);
            }
        }
        catch (Exception)
        {
            // Missing (DllNotFoundException), built for another CPU (BadImageFormatException), an
            // older API (EntryPointNotFoundException) or anything else: the app runs on without it.
            return (false, 0);
        }
    }

    /// <summary>
    /// Runs a voice-like sweep through RNNoise and finds the whole-frame lag at which the output
    /// matches the input best. Null when nothing usable comes out.
    /// </summary>
    private static unsafe int? MeasureDelay(nint state)
    {
        const int frames = 200;           // 2 s: 0.5 s of silence, then the sweep
        const int silentFrames = 50;
        const int maxLagFrames = 3;
        int length = frames * FrameSize;
        var input = new float[length];
        var output = new float[length];
        for (int i = silentFrames * FrameSize; i < length; i++)
        {
            double t = (i - silentFrames * FrameSize) / 48000.0;
            double sweep = Math.Sin(2 * Math.PI * (150 + 400 * t) * t);
            double syllables = (1 + Math.Sin(2 * Math.PI * 4 * t)) / 2;
            input[i] = (float)(sweep * syllables * 0.5 * Scale);
        }

        fixed (float* pin = input, pout = output)
        {
            for (int f = 0; f < frames; f++)
            {
                rnnoise_process_frame(state, pout + f * FrameSize, pin + f * FrameSize);
            }
        }

        int best = -1;
        double bestScore = 0;
        for (int lagFrames = 0; lagFrames <= maxLagFrames; lagFrames++)
        {
            int lag = lagFrames * FrameSize;
            double score = 0;
            for (int i = silentFrames * FrameSize; i < length - maxLagFrames * FrameSize; i++)
            {
                score += (double)input[i] * output[i + lag];
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = lag;
            }
        }

        return best < 0 ? null : best;
    }
}
