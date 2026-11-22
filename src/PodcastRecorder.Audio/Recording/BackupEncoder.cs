using NAudio.Wave;

namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// The safety copy written next to every recording: a small MP3 that stays playable however the
/// app ends, because MP3 needs no header written at the end. It is pushed to the disk every few
/// seconds, so a crash or a power cut loses at most those seconds. Where LAME is missing (macOS
/// or Linux without libmp3lame) it falls back to FLAC, whose frames also decode without the end.
/// </summary>
public sealed class BackupEncoder : IAudioEncoder
{
    public const int Mp3BitrateKbps = 96;

    /// <summary>How much audio may sit in memory before it is forced to the disk.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly IAudioEncoder? _flac;
    private FileStream? _file;
    private Stream? _mp3;
    private byte[] _pcm = [];
    private long _samplesSinceFlush;
    private long _flushSamples;

    public BackupEncoder() : this(Mp3Writers.IsAvailable)
    {
    }

    /// <param name="mp3">False writes FLAC instead (also used when LAME cannot be loaded).</param>
    public BackupEncoder(bool mp3)
    {
        IsMp3 = mp3;
        if (!mp3) _flac = new FlacEncoder();
    }

    public bool IsMp3 { get; }

    public string FileExtension => IsMp3 ? ".mp3" : ".flac";

    /// <summary>Roughly how much an hour of backup takes on the disk.</summary>
    public long BytesPerHour => IsMp3
        ? Mp3BitrateKbps * 1000L / 8 * 3600
        : EncoderCatalog.EstimatedBytesPerHour("flac");

    public void Open(string path, WaveFormat inputFormat)
    {
        _flushSamples = (long)(FlushInterval.TotalSeconds * inputFormat.SampleRate * inputFormat.Channels);
        if (_flac is not null)
        {
            _flac.Open(path, inputFormat);
            return;
        }

        // Owning the file (instead of letting the MP3 writer open it) lets it be flushed to the disk.
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        try
        {
            _mp3 = Mp3Writers.Create(_file, new WaveFormat(inputFormat.SampleRate, 16, inputFormat.Channels), Mp3BitrateKbps);
        }
        catch
        {
            _file.Dispose();
            _file = null;
            File.Delete(path);
            throw;
        }
    }

    public void Write(float[] samples, int offset, int count)
    {
        if (_flac is not null)
        {
            _flac.Write(samples, offset, count);
            return;
        }

        var mp3 = _mp3 ?? throw new InvalidOperationException("Encoder is not open.");
        int bytes = PcmConversion.ToPcm16(samples.AsSpan(offset, count), ref _pcm);
        mp3.Write(_pcm, 0, bytes);

        _samplesSinceFlush += count;
        if (_samplesSinceFlush >= _flushSamples)
        {
            _samplesSinceFlush = 0;
            // Only the file: flushing the MP3 writer itself would end the MP3 stream.
            _file!.Flush(flushToDisk: true);
        }
    }

    public void Dispose()
    {
        _flac?.Dispose();
        if (_mp3 is not null)
        {
            _mp3.Flush();
            _mp3.Dispose();
            _mp3 = null;
        }

        _file?.Dispose();
        _file = null;
    }
}
