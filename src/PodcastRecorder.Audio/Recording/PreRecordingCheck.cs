using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.Audio.Recording;

public enum CheckStatus
{
    Ok,
    Warning,
    Problem,
}

public enum LevelIssue
{
    None,

    /// <summary>No speech came through: wrong device, muted channel or no microphone permission.</summary>
    NoSignal,
    TooQuiet,
    TooLoud,
    Clipping,
}

public enum NoiseIssue
{
    None,
    Noticeable,
    Loud,

    /// <summary>The voice is not far enough above the room noise.</summary>
    LowSeparation,

    /// <summary>Nothing came through the microphone, so the room could not be judged.</summary>
    NotMeasured,
}

/// <summary>One microphone's result: levels in dBFS (RMS of 50 ms windows; the peak is a sample peak).</summary>
public sealed record MicrophoneCheck(
    double SpeechDb,
    double NoiseDb,
    double PeakDb,
    CheckStatus LevelStatus,
    LevelIssue LevelIssue,
    CheckStatus NoiseStatus,
    NoiseIssue NoiseIssue)
{
    /// <summary>How far the voice is above the room noise.</summary>
    public double SeparationDb => SpeechDb - NoiseDb;
}

/// <summary>Free space where the recording goes and roughly how long a recording fits into it.</summary>
public sealed record DiskCheck(long FreeBytes, TimeSpan RecordingTime, CheckStatus Status);

/// <summary>
/// The ten-second test before a recording: a few seconds of silence to hear the room, then a few
/// seconds of speech to set the microphone level, plus the free disk space.
/// </summary>
public static class PreRecordingCheck
{
    public static readonly TimeSpan QuietPhase = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan SpeechPhase = TimeSpan.FromSeconds(7);

    private const double Silence = -100;

    /// <summary>Loud speech windows below this sound thin; the listener has to turn up.</summary>
    private const double QuietSpeechDb = -32;

    /// <summary>Loud speech windows above this leave too little room before clipping.</summary>
    private const double LoudSpeechDb = -8;

    /// <summary>A sample this close to full scale is taken as clipping.</summary>
    private const double ClippingPeakDb = -0.3;

    /// <summary>Below this the "speech" is noise, or nothing at all.</summary>
    private const double NoSignalDb = -60;

    private const double NoticeableNoiseDb = -50;
    private const double LoudNoiseDb = -40;
    private const double MinimumSeparationDb = 25;

    /// <summary>Space kept free for the file system and the tags and chapters written at the end.</summary>
    private const long Reserve = 500L * 1024 * 1024;

    public static MicrophoneCheck AnalyzeMicrophone(LevelReading quiet, LevelReading speech)
    {
        // The room: the median of the quiet windows, so a cough or a click does not count.
        double noise = ToDb(Percentile(quiet.WindowRms, 0.5));
        // The voice: the louder windows of the speech, since pauses between words pull an average down.
        double voice = ToDb(Percentile(speech.WindowRms, 0.9));
        double peak = ToDb(Math.Max(quiet.Peak, speech.Peak));

        var (levelStatus, levelIssue) =
            voice < NoSignalDb || voice - noise < 6 ? (CheckStatus.Problem, LevelIssue.NoSignal)
            : peak >= ClippingPeakDb ? (CheckStatus.Problem, LevelIssue.Clipping)
            : voice > LoudSpeechDb ? (CheckStatus.Warning, LevelIssue.TooLoud)
            : voice < QuietSpeechDb ? (CheckStatus.Warning, LevelIssue.TooQuiet)
            : (CheckStatus.Ok, LevelIssue.None);

        var (noiseStatus, noiseIssue) =
            levelIssue == LevelIssue.NoSignal ? (CheckStatus.Warning, NoiseIssue.NotMeasured)
            : noise > LoudNoiseDb ? (CheckStatus.Problem, NoiseIssue.Loud)
            : noise > NoticeableNoiseDb ? (CheckStatus.Warning, NoiseIssue.Noticeable)
            : voice - noise < MinimumSeparationDb ? (CheckStatus.Warning, NoiseIssue.LowSeparation)
            : (CheckStatus.Ok, NoiseIssue.None);

        return new MicrophoneCheck(voice, noise, peak, levelStatus, levelIssue, noiseStatus, noiseIssue);
    }

    /// <summary>Judges the free space against what an hour of recording takes with the chosen settings.</summary>
    public static DiskCheck AnalyzeDisk(long freeBytes, long bytesPerHour)
    {
        long usable = Math.Max(0, freeBytes - Reserve);
        var time = bytesPerHour > 0 ? TimeSpan.FromHours((double)usable / bytesPerHour) : TimeSpan.MaxValue;
        var status = time >= TimeSpan.FromHours(3) ? CheckStatus.Ok
            : time >= TimeSpan.FromHours(1) ? CheckStatus.Warning
            : CheckStatus.Problem;
        return new DiskCheck(freeBytes, time, status);
    }

    /// <summary>
    /// Free bytes on the volume that holds <paramref name="folder"/> (which need not exist yet),
    /// or null when it cannot be found.
    /// </summary>
    public static long? FreeSpaceFor(string folder)
    {
        try
        {
            var path = Path.GetFullPath(folder);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            // The volume whose root is the longest prefix of the path: on macOS and Linux "/" holds
            // everything, but the home folder can be on another mount.
            var drive = DriveInfo.GetDrives()
                .Where(d => IsReady(d) && IsUnder(path, d.RootDirectory.FullName, comparison))
                .MaxBy(d => d.RootDirectory.FullName.Length);
            return drive?.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsReady(DriveInfo drive)
    {
        try
        {
            return drive.IsReady;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string root, StringComparison comparison)
    {
        if (!path.StartsWith(root, comparison)) return false;
        // "/Volumes/Data" is not under "/Volumes/D".
        return path.Length == root.Length
            || root.EndsWith(Path.DirectorySeparatorChar)
            || path[root.Length] == Path.DirectorySeparatorChar;
    }

    private static double Percentile(IReadOnlyList<float> values, double fraction)
    {
        if (values.Count == 0) return 0;
        var sorted = values.Order().ToArray();
        int index = (int)Math.Round(fraction * (sorted.Length - 1));
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static double ToDb(double level) => level > 1e-5 ? 20 * Math.Log10(level) : Silence;
}
