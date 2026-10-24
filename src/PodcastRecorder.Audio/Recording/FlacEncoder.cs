using System.Buffers.Binary;
using System.Security.Cryptography;
using NAudio.Wave;
using PodcastRecorder.Audio.Recording.Flac;

namespace PodcastRecorder.Audio.Recording;

/// <summary>
/// Lossless 16-bit FLAC, written by a small built-in encoder (fixed predictors, Rice coding,
/// stereo decorrelation). Files are typically about half the size of WAV.
/// </summary>
public sealed class FlacEncoder : IAudioEncoder
{
    private const int BlockSize = 4096;
    private const int BitsPerSample = 16;
    private const int StreamInfoOffset = 4;

    private readonly BitWriter _frame = new();
    private readonly SubframeEncoder[] _subframes = [new(), new(), new(), new()];
    private FileStream? _stream;
    private IncrementalHash? _md5;
    private int[][] _block = [];
    private readonly int[] _mid = new int[BlockSize];
    private readonly int[] _side = new int[BlockSize];
    private byte[] _md5Buffer = [];
    private int _channels;
    private int _sampleRate;
    private int _filled;
    private long _totalFrames;
    private ulong _frameNumber;
    private int _minFrameBytes = int.MaxValue;
    private int _maxFrameBytes;

    public string FileExtension => ".flac";

    public void Open(string path, WaveFormat inputFormat)
    {
        if (inputFormat.Channels is < 1 or > 2)
        {
            throw new NotSupportedException("FLAC encoder supports mono and stereo.");
        }

        _channels = inputFormat.Channels;
        _sampleRate = inputFormat.SampleRate;
        _block = Enumerable.Range(0, _channels).Select(_ => new int[BlockSize]).ToArray();
        _md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        _stream.Write("fLaC"u8);
        _stream.Write(StreamInfo(md5: new byte[16]));
    }

    public void Write(float[] samples, int offset, int count)
    {
        if (_stream is null) throw new InvalidOperationException("Encoder is not open.");

        int frames = count / _channels;
        if (_md5Buffer.Length < frames * _channels * 2) _md5Buffer = new byte[frames * _channels * 2];

        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < _channels; c++)
            {
                short value = (short)(Math.Clamp(samples[offset + f * _channels + c], -1f, 1f) * short.MaxValue);
                _block[c][_filled] = value;
                BinaryPrimitives.WriteInt16LittleEndian(_md5Buffer.AsSpan((f * _channels + c) * 2), value);
            }

            if (++_filled == BlockSize) EncodeFrame();
        }

        _md5!.AppendData(_md5Buffer, 0, frames * _channels * 2);
        _totalFrames += frames;
    }

    public void Dispose()
    {
        if (_stream is null) return;

        if (_filled > 0) EncodeFrame();
        _stream.Position = StreamInfoOffset;
        _stream.Write(StreamInfo(_md5!.GetHashAndReset()));
        _stream.Dispose();
        _md5.Dispose();
        _stream = null;
    }

    private void EncodeFrame()
    {
        int n = _filled;
        int assignment = PlanChannels(n);

        _frame.Reset();
        _frame.Write(0b11111111111110, 14);
        _frame.Write(0, 1);
        _frame.Write(0, 1);
        _frame.Write(n == BlockSize ? 0b1100u : 0b0111u, 4);
        _frame.Write(SampleRateCode(), 4);
        _frame.Write((uint)assignment, 4);
        _frame.Write(0b100, 3);
        _frame.Write(0, 1);
        _frame.WriteUtf8(_frameNumber);
        if (n != BlockSize) _frame.Write((uint)(n - 1), 16);
        _frame.Write(FlacCrc.Crc8(_frame.Written), 8);

        foreach (var subframe in SubframesFor(assignment)) subframe.Write(_frame);

        _frame.PadToByte();
        _frame.Write(FlacCrc.Crc16(_frame.Written), 16);

        var bytes = _frame.Written;
        _stream!.Write(bytes);
        _minFrameBytes = Math.Min(_minFrameBytes, bytes.Length);
        _maxFrameBytes = Math.Max(_maxFrameBytes, bytes.Length);
        _frameNumber++;
        _filled = 0;
    }

    /// <summary>
    /// Plans every subframe candidate and returns the cheapest channel assignment:
    /// independent, left/side, right/side or mid/side.
    /// </summary>
    private int PlanChannels(int n)
    {
        var left = _subframes[0];
        left.Plan(_block[0], n, BitsPerSample);
        if (_channels == 1) return 0b0000;

        var right = _subframes[1];
        right.Plan(_block[1], n, BitsPerSample);
        for (int i = 0; i < n; i++)
        {
            int l = _block[0][i], r = _block[1][i];
            _side[i] = l - r;
            _mid[i] = (l + r) >> 1;
        }

        var mid = _subframes[2];
        var side = _subframes[3];
        mid.Plan(_mid, n, BitsPerSample);
        side.Plan(_side, n, BitsPerSample + 1);

        long independent = left.Bits + right.Bits;
        long leftSide = left.Bits + side.Bits;
        long rightSide = side.Bits + right.Bits;
        long midSide = mid.Bits + side.Bits;
        long best = Math.Min(Math.Min(independent, leftSide), Math.Min(rightSide, midSide));

        if (best == independent) return 0b0001;
        if (best == leftSide) return 0b1000;
        if (best == rightSide) return 0b1001;
        return 0b1010;
    }

    private SubframeEncoder[] SubframesFor(int assignment) => assignment switch
    {
        0b0000 => [_subframes[0]],
        0b0001 => [_subframes[0], _subframes[1]],
        0b1000 => [_subframes[0], _subframes[3]],
        0b1001 => [_subframes[3], _subframes[1]],
        _ => [_subframes[2], _subframes[3]],
    };

    private uint SampleRateCode() => _sampleRate switch
    {
        44100 => 0b1001,
        48000 => 0b1010,
        96000 => 0b1011,
        _ => 0b0000, // taken from STREAMINFO
    };

    private byte[] StreamInfo(byte[] md5)
    {
        var writer = new BitWriter();
        writer.Write(1, 1); // last metadata block
        writer.Write(0, 7); // STREAMINFO
        writer.Write(34, 24);
        writer.Write(BlockSize, 16);
        writer.Write(BlockSize, 16);
        writer.Write(_maxFrameBytes == 0 ? 0u : (uint)_minFrameBytes, 24);
        writer.Write((uint)_maxFrameBytes, 24);
        writer.Write((uint)_sampleRate, 20);
        writer.Write((uint)(_channels - 1), 3);
        writer.Write(BitsPerSample - 1, 5);
        writer.Write((uint)(_totalFrames >> 32), 4);
        writer.Write((uint)_totalFrames, 32);
        foreach (var b in md5) writer.Write(b, 8);
        return writer.Written.ToArray();
    }
}
