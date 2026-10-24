using NAudio.MediaFoundation;
using NAudio.Wave;

namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// WMA via the Windows Media Foundation encoder. Media Foundation encodes whole files, so the
/// recording is written to a WAV file next to the target and converted when it is closed. If
/// the conversion fails the WAV file is kept, so the recording is never lost.
/// </summary>
public sealed class WmaEncoder(int bitrateKbps) : IAudioEncoder
{
    private WaveFileWriter? _writer;
    private string? _path;
    private string? _wavPath;

    public int BitrateKbps { get; } = bitrateKbps;

    public string FileExtension => ".wma";

    public void Open(string path, WaveFormat inputFormat)
    {
        var pcmFormat = new WaveFormat(inputFormat.SampleRate, 16, inputFormat.Channels);
        // Check for the encoder now so a missing WMA encoder fails at the start, not after the show.
        // The media type itself is chosen again in Dispose: Media Foundation objects must stay on
        // the COM apartment that created them, and Open and Dispose run on different threads.
        SelectMediaType(pcmFormat);
        _path = path;
        _wavPath = path + ".kayit.wav";
        _writer = new WaveFileWriter(_wavPath, pcmFormat);
    }

    public void Write(float[] samples, int offset, int count)
    {
        var writer = _writer ?? throw new InvalidOperationException("Encoder is not open.");
        writer.WriteSamples(samples, offset, count);
    }

    /// <summary>Closes the temporary WAV file and converts it to WMA. Can take a while for long recordings.</summary>
    public void Dispose()
    {
        if (_writer is null || _path is null || _wavPath is null)
        {
            return;
        }

        var pcmFormat = _writer.WaveFormat;
        _writer.Dispose();
        _writer = null;
        try
        {
            using (var reader = new WaveFileReader(_wavPath))
            using (var encoder = new MediaFoundationEncoder(SelectMediaType(pcmFormat)))
            {
                encoder.Encode(_path, reader);
            }

            File.Delete(_wavPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (File.Exists(_path)) File.Delete(_path);
            var kept = Path.ChangeExtension(_path, ".wav");
            File.Move(_wavPath, kept, overwrite: false);
            throw new IOException($"WMA'ya çevrilemedi, kayıt WAV olarak saklandı: {kept}", ex);
        }
    }

    private MediaType SelectMediaType(WaveFormat pcmFormat)
    {
        MediaFoundationApi.Startup();
        return MediaFoundationEncoder.SelectMediaType(AudioSubtypes.MFAudioFormat_WMAudioV8, pcmFormat, BitrateKbps * 1000)
            ?? throw new InvalidOperationException("Bu bilgisayarda WMA kodlayıcısı bulunamadı.");
    }
}
