using NAudio.Wave;
using PodcastRecorder.Audio.Mixing;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

public class PreRecordingCheckTests
{
    private static LevelReading Steady(double rmsDb, int windows = 60, double? peakDb = null)
    {
        float rms = (float)Math.Pow(10, rmsDb / 20);
        float peak = (float)Math.Pow(10, (peakDb ?? rmsDb + 10) / 20);
        return new LevelReading(Enumerable.Repeat(rms, windows).ToArray(), peak);
    }

    [Fact]
    public void Probe_measures_rms_in_50_ms_windows_and_keeps_the_peak()
    {
        var probe = new LevelProbe();
        // One second of a 0.5 square wave (stereo) with a single 0.9 spike.
        var block = Enumerable.Repeat(0.5f, 48_000 * 2).ToArray();
        block[1000] = -0.9f;
        probe.Process(block, 2);

        var reading = probe.TakeReading();
        Assert.Equal(20, reading.WindowRms.Count);
        Assert.Equal(0.5f, reading.WindowRms[5], 3);
        Assert.Equal(0.9f, reading.Peak, 3);

        Assert.Empty(probe.TakeReading().WindowRms);
    }

    [Fact]
    public void Good_voice_in_a_quiet_room_passes()
    {
        var check = PreRecordingCheck.AnalyzeMicrophone(Steady(-65), Steady(-18, peakDb: -6));

        Assert.Equal(CheckStatus.Ok, check.LevelStatus);
        Assert.Equal(CheckStatus.Ok, check.NoiseStatus);
        Assert.Equal(-18, check.SpeechDb, 1);
        Assert.Equal(-65, check.NoiseDb, 1);
        Assert.Equal(47, check.SeparationDb, 1);
    }

    [Fact]
    public void Pauses_between_words_do_not_make_the_voice_look_quiet()
    {
        // Half of the speech phase is silence between words; the louder windows count.
        var speech = new LevelReading(
            [.. Enumerable.Repeat(0.0005f, 70), .. Enumerable.Repeat(0.12f, 70)], 0.4f);

        var check = PreRecordingCheck.AnalyzeMicrophone(Steady(-66), speech);

        Assert.Equal(LevelIssue.None, check.LevelIssue);
    }

    [Theory]
    [InlineData(-40, LevelIssue.TooQuiet, CheckStatus.Warning)]
    [InlineData(-5, LevelIssue.TooLoud, CheckStatus.Warning)]
    [InlineData(-90, LevelIssue.NoSignal, CheckStatus.Problem)]
    public void Flags_the_voice_level(double speechDb, LevelIssue issue, CheckStatus status)
    {
        var check = PreRecordingCheck.AnalyzeMicrophone(Steady(-95), Steady(speechDb, peakDb: speechDb + 4));

        Assert.Equal(issue, check.LevelIssue);
        Assert.Equal(status, check.LevelStatus);
    }

    [Fact]
    public void Clipping_is_a_problem()
    {
        var check = PreRecordingCheck.AnalyzeMicrophone(Steady(-65), Steady(-14, peakDb: 0));

        Assert.Equal(LevelIssue.Clipping, check.LevelIssue);
        Assert.Equal(CheckStatus.Problem, check.LevelStatus);
    }

    [Fact]
    public void Silent_microphone_does_not_report_a_quiet_room()
    {
        var check = PreRecordingCheck.AnalyzeMicrophone(LevelReading.Empty, LevelReading.Empty);

        Assert.Equal(LevelIssue.NoSignal, check.LevelIssue);
        Assert.Equal(NoiseIssue.NotMeasured, check.NoiseIssue);
    }

    [Theory]
    [InlineData(-45, NoiseIssue.Noticeable, CheckStatus.Warning)]
    [InlineData(-35, NoiseIssue.Loud, CheckStatus.Problem)]
    public void Flags_room_noise(double noiseDb, NoiseIssue issue, CheckStatus status)
    {
        var check = PreRecordingCheck.AnalyzeMicrophone(Steady(noiseDb), Steady(-14));

        Assert.Equal(issue, check.NoiseIssue);
        Assert.Equal(status, check.NoiseStatus);
    }

    [Fact]
    public void A_cough_in_the_quiet_phase_does_not_count_as_noise()
    {
        var quiet = new LevelReading([.. Enumerable.Repeat(0.0005f, 55), .. Enumerable.Repeat(0.3f, 5)], 0.6f);

        var check = PreRecordingCheck.AnalyzeMicrophone(quiet, Steady(-18, peakDb: -6));

        Assert.Equal(NoiseIssue.None, check.NoiseIssue);
    }

    [Fact]
    public void Quiet_voice_over_low_noise_is_flagged_as_low_separation()
    {
        var check = PreRecordingCheck.AnalyzeMicrophone(Steady(-52 - 0.5), Steady(-30));

        Assert.Equal(NoiseIssue.LowSeparation, check.NoiseIssue);
    }

    [Fact]
    public void Disk_space_is_judged_in_hours_of_recording()
    {
        const long gb = 1L << 30;
        long perHour = EncoderCatalog.EstimatedBytesPerHour("flac");

        Assert.Equal(CheckStatus.Ok, PreRecordingCheck.AnalyzeDisk(100 * gb, perHour).Status);
        Assert.Equal(CheckStatus.Warning, PreRecordingCheck.AnalyzeDisk(gb, perHour).Status);
        Assert.Equal(CheckStatus.Problem, PreRecordingCheck.AnalyzeDisk(gb / 2, perHour).Status);
        Assert.Equal(TimeSpan.Zero, PreRecordingCheck.AnalyzeDisk(gb / 4, perHour).RecordingTime);
    }

    [Fact]
    public void Estimates_bytes_per_hour_from_the_format()
    {
        Assert.Equal(86_400_000, EncoderCatalog.EstimatedBytesPerHour("mp3-192"));
        Assert.Equal(691_200_000, EncoderCatalog.EstimatedBytesPerHour("wav"));
        Assert.True(EncoderCatalog.EstimatedBytesPerHour("flac") < EncoderCatalog.EstimatedBytesPerHour("wav"));
        foreach (var option in EncoderCatalog.All) Assert.True(EncoderCatalog.EstimatedBytesPerHour(option.Id) > 0);
    }

    [Fact]
    public void Finds_free_space_for_a_folder_that_does_not_exist_yet()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"podcastrecorder-{Guid.NewGuid():N}", "Kayıtlar");

        Assert.True(PreRecordingCheck.FreeSpaceFor(folder) > 0);
    }
}

public class BackupRecordingTests
{
    [Fact]
    public async Task Recorder_writes_the_backup_alongside_the_mix()
    {
        var recorder = new Recorder();
        var mix = new CapturingEncoder();
        var backup = new CapturingEncoder();
        recorder.Start(new RecordingTarget("episode.raw", mix), backup: new RecordingTarget("backup.raw", backup));

        recorder.Write(new float[960]);
        recorder.Pause();
        recorder.Write(new float[960]);
        recorder.Resume();
        recorder.Write(new float[960]);
        await recorder.StopAsync();

        Assert.Equal("backup.raw", recorder.BackupPath);
        Assert.Equal(1920, backup.Samples.Count);
        Assert.True(backup.Disposed);
        Assert.Equal(TimeSpan.FromMilliseconds(20), recorder.Elapsed);
    }

    [Fact]
    public async Task A_backup_that_cannot_open_does_not_stop_the_recording()
    {
        var recorder = new Recorder();
        var mix = new CapturingEncoder();
        recorder.Start(new RecordingTarget("episode.raw", mix), backup: new RecordingTarget("backup.raw", new FailingEncoder()));

        recorder.Write(new float[960]);
        await recorder.StopAsync();

        Assert.Null(recorder.BackupPath);
        Assert.IsType<IOException>(recorder.BackupError);
        Assert.Equal(960, mix.Samples.Count);
    }

    [Fact]
    public void Mp3_backup_plays_while_still_being_written()
    {
        if (!Mp3Writers.IsAvailable) return;

        var path = Path.Combine(Path.GetTempPath(), $"podcastrecorder-{Guid.NewGuid():N}.mp3");
        var copy = path + ".copy.mp3";
        var encoder = new BackupEncoder(mp3: true);
        try
        {
            encoder.Open(path, AudioFormat.Mix);
            var block = new float[4800 * 2];
            for (int i = 0; i < block.Length; i++) block[i] = 0.3f * MathF.Sin(i / 2 * 0.05f);
            // Six seconds, so the five-second flush has pushed the audio to the disk.
            for (int n = 0; n < 60; n++) encoder.Write(block, 0, block.Length);

            // As if the app had crashed here: the file is never closed.
            File.Copy(path, copy);
            using var reader = PodcastRecorder.Audio.Sources.AudioFiles.Open(copy);
            Assert.True(reader.TotalTime > TimeSpan.FromSeconds(4.5), $"only {reader.TotalTime}");
        }
        finally
        {
            encoder.Dispose();
            File.Delete(path);
            File.Delete(copy);
        }
    }

    [Fact]
    public void Flac_backup_is_used_when_mp3_is_not_available()
    {
        var path = Path.Combine(Path.GetTempPath(), $"podcastrecorder-{Guid.NewGuid():N}.flac");
        var encoder = new BackupEncoder(mp3: false);
        try
        {
            Assert.Equal(".flac", encoder.FileExtension);
            encoder.Open(path, AudioFormat.Mix);
            encoder.Write(new float[9600], 0, 9600);
            encoder.Dispose();
            Assert.True(new FileInfo(path).Length > 42);
        }
        finally
        {
            encoder.Dispose();
            File.Delete(path);
        }
    }

    private sealed class FailingEncoder : IAudioEncoder
    {
        public string FileExtension => ".raw";

        public void Open(string path, WaveFormat inputFormat) => throw new IOException("disk full");

        public void Write(float[] samples, int offset, int count)
        {
        }

        public void Dispose()
        {
        }
    }
}
