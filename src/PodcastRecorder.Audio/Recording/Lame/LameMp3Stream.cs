using NAudio.Wave;

namespace PodcastRecorder.Audio.Recording.Lame;

/// <summary>
/// Takes interleaved 16-bit PCM and writes constant-bitrate MP3 to <c>output</c>, like NAudio's
/// LameMP3FileWriter but on the system's libmp3lame.
/// </summary>
internal sealed class LameMp3Stream : Stream
{
    private readonly Stream _output;
    private readonly bool _ownsOutput;
    private readonly int _channels;
    private IntPtr _lame;
    private short[] _pcm = [];
    private byte[] _mp3 = [];

    public LameMp3Stream(Stream output, WaveFormat format, int bitrateKbps, bool ownsOutput)
    {
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16)
        {
            throw new ArgumentException("LAME needs 16-bit PCM.", nameof(format));
        }

        LameNative.EnsureAvailable();
        _output = output;
        _ownsOutput = ownsOutput;
        _channels = format.Channels;
        _lame = LameNative.lame_init();
        if (_lame == IntPtr.Zero) throw new InvalidOperationException(Texts.Get("Lame_InitFailed"));
        LameNative.lame_set_in_samplerate(_lame, format.SampleRate);
        LameNative.lame_set_out_samplerate(_lame, format.SampleRate);
        LameNative.lame_set_num_channels(_lame, format.Channels);
        LameNative.lame_set_VBR(_lame, 0);
        LameNative.lame_set_brate(_lame, bitrateKbps);
        LameNative.lame_set_quality(_lame, 2);
        if (LameNative.lame_init_params(_lame) < 0)
        {
            LameNative.lame_close(_lame);
            _lame = IntPtr.Zero;
            throw new ArgumentException(Texts.Format("Lame_BadSettings", format.SampleRate, bitrateKbps));
        }
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_lame == IntPtr.Zero, this);
        int samples = count / 2;
        if (_pcm.Length < samples) _pcm = new short[samples];
        Buffer.BlockCopy(buffer, offset, _pcm, 0, samples * 2);

        int frames = samples / _channels;
        EnsureMp3Buffer(frames);
        int bytes = LameNative.lame_encode_buffer_interleaved(_lame, _pcm, frames, _mp3, _mp3.Length);
        if (bytes < 0) throw new IOException(Texts.Format("Lame_EncodeFailed", bytes));
        _output.Write(_mp3, 0, bytes);
    }

    public override void Flush() => _output.Flush();

    protected override void Dispose(bool disposing)
    {
        if (_lame != IntPtr.Zero)
        {
            try
            {
                EnsureMp3Buffer(0);
                int bytes = LameNative.lame_encode_flush(_lame, _mp3, _mp3.Length);
                if (disposing && bytes > 0) _output.Write(_mp3, 0, bytes);
                if (disposing) _output.Flush();
            }
            finally
            {
                LameNative.lame_close(_lame);
                _lame = IntPtr.Zero;
                if (disposing && _ownsOutput) _output.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>LAME's worst case: 1.25 bytes per sample plus 7200.</summary>
    private void EnsureMp3Buffer(int frames)
    {
        int size = frames * 5 / 4 + 7200;
        if (_mp3.Length < size) _mp3 = new byte[size];
    }
}
