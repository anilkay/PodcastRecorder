using System.Runtime.InteropServices;

namespace PodcastRecorder.Audio.PortAudio;

/// <summary>The parts of the PortAudio C API the backend uses (portaudio.h, v19).</summary>
internal static unsafe partial class PortAudioNative
{
    private const string Library = "portaudio";

    public const int NoDevice = -1;
    public const ulong Float32 = 0x00000001;
    public const int Continue = 0;
    public const int Abort = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfo
    {
        public int StructVersion;
        public IntPtr Name;
        public int HostApi;
        public int MaxInputChannels;
        public int MaxOutputChannels;
        public double DefaultLowInputLatency;
        public double DefaultLowOutputLatency;
        public double DefaultHighInputLatency;
        public double DefaultHighOutputLatency;
        public double DefaultSampleRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HostApiInfo
    {
        public int StructVersion;
        public int Type;
        public IntPtr Name;
        public int DeviceCount;
        public int DefaultInputDevice;
        public int DefaultOutputDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StreamParameters
    {
        public int Device;
        public int ChannelCount;

        /// <summary>C "unsigned long": 4 bytes on Windows, 8 on Linux and macOS.</summary>
        public CULong SampleFormat;
        public double SuggestedLatency;
        public IntPtr HostApiSpecificStreamInfo;
    }

    [LibraryImport(Library)]
    public static partial int Pa_Initialize();

    [LibraryImport(Library)]
    public static partial int Pa_Terminate();

    [LibraryImport(Library)]
    public static partial IntPtr Pa_GetErrorText(int error);

    [LibraryImport(Library)]
    public static partial int Pa_GetDeviceCount();

    [LibraryImport(Library)]
    public static partial int Pa_GetDefaultInputDevice();

    [LibraryImport(Library)]
    public static partial int Pa_GetDefaultOutputDevice();

    [LibraryImport(Library)]
    public static partial DeviceInfo* Pa_GetDeviceInfo(int device);

    [LibraryImport(Library)]
    public static partial HostApiInfo* Pa_GetHostApiInfo(int hostApi);

    [LibraryImport(Library)]
    public static partial int Pa_IsFormatSupported(StreamParameters* input, StreamParameters* output, double sampleRate);

    [LibraryImport(Library)]
    public static partial int Pa_OpenStream(
        out IntPtr stream,
        StreamParameters* input,
        StreamParameters* output,
        double sampleRate,
        CULong framesPerBuffer,
        CULong flags,
        delegate* unmanaged[Cdecl]<void*, void*, CULong, void*, CULong, void*, int> callback,
        void* userData);

    [LibraryImport(Library)]
    public static partial int Pa_StartStream(IntPtr stream);

    [LibraryImport(Library)]
    public static partial int Pa_StopStream(IntPtr stream);

    [LibraryImport(Library)]
    public static partial int Pa_AbortStream(IntPtr stream);

    [LibraryImport(Library)]
    public static partial int Pa_CloseStream(IntPtr stream);

    [LibraryImport(Library)]
    public static partial int Pa_IsStreamActive(IntPtr stream);

    public static string Text(IntPtr utf8) => Marshal.PtrToStringUTF8(utf8) ?? "";

    /// <summary>Throws with PortAudio's message when <paramref name="error"/> is negative.</summary>
    public static void Check(int error, string what)
    {
        if (error < 0) throw new InvalidOperationException($"{what}: {Text(Pa_GetErrorText(error))}");
    }
}
