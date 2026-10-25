using System.Reflection;
using System.Runtime.InteropServices;

namespace PodcastRecorder.Audio.Recording.Lame;

/// <summary>
/// The system's libmp3lame, for Linux and macOS (NAudio.Lame bundles a Windows DLL only).
/// Install it with "sudo apt install libmp3lame0" or "brew install lame".
/// </summary>
internal static class LameNative
{
    private const string Library = "mp3lame";

    private static readonly string[] Candidates =
    [
        "libmp3lame.so.0",
        "libmp3lame.so",
        "libmp3lame.0.dylib",
        "libmp3lame.dylib",
        "/opt/homebrew/lib/libmp3lame.dylib",
        "/usr/local/lib/libmp3lame.dylib",
    ];

    static LameNative() => NativeLibrary.SetDllImportResolver(typeof(LameNative).Assembly, Resolve);

    /// <summary>Throws a readable error when the library is missing, before anything is written.</summary>
    public static void EnsureAvailable()
    {
        if (Resolve(Library, typeof(LameNative).Assembly, null) == IntPtr.Zero)
        {
            throw new DllNotFoundException(
                Texts.Get("Lame_Missing"));
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return IntPtr.Zero;
        foreach (var candidate in Candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle)) return handle;
        }

        return IntPtr.Zero;
    }

    [DllImport(Library)]
    public static extern IntPtr lame_init();

    [DllImport(Library)]
    public static extern int lame_set_in_samplerate(IntPtr flags, int rate);

    [DllImport(Library)]
    public static extern int lame_set_out_samplerate(IntPtr flags, int rate);

    [DllImport(Library)]
    public static extern int lame_set_num_channels(IntPtr flags, int channels);

    [DllImport(Library)]
    public static extern int lame_set_brate(IntPtr flags, int kbps);

    [DllImport(Library)]
    public static extern int lame_set_VBR(IntPtr flags, int mode);

    [DllImport(Library)]
    public static extern int lame_set_quality(IntPtr flags, int quality);

    [DllImport(Library)]
    public static extern int lame_init_params(IntPtr flags);

    [DllImport(Library)]
    public static extern int lame_encode_buffer_interleaved(IntPtr flags, short[] pcm, int samplesPerChannel, byte[] mp3, int mp3Size);

    [DllImport(Library)]
    public static extern int lame_encode_flush(IntPtr flags, byte[] mp3, int mp3Size);

    [DllImport(Library)]
    public static extern int lame_close(IntPtr flags);
}
