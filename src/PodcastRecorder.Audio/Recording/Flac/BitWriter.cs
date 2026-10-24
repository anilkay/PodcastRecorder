namespace PodcastRecorder.Audio.Recording.Flac;

/// <summary>Writes big-endian bit fields, as FLAC frames are laid out.</summary>
internal sealed class BitWriter
{
    private byte[] _buffer = new byte[64 * 1024];
    private int _bytes;
    private ulong _accumulator;
    private int _bits;

    public int ByteCount => _bytes;

    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _bytes);

    public void Reset()
    {
        _bytes = 0;
        _accumulator = 0;
        _bits = 0;
    }

    /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/>, up to 32.</summary>
    public void Write(uint value, int count)
    {
        if (count == 0) return;
        _accumulator = (_accumulator << count) | (value & (uint)((1UL << count) - 1));
        _bits += count;
        while (_bits >= 8)
        {
            _bits -= 8;
            Put((byte)(_accumulator >> _bits));
        }
    }

    public void WriteSigned(int value, int count) => Write((uint)value, count);

    /// <summary>Writes <paramref name="count"/> zero bits followed by a one bit.</summary>
    public void WriteUnary(uint count)
    {
        while (count >= 32)
        {
            Write(0, 32);
            count -= 32;
        }

        Write(1, (int)count + 1);
    }

    public void WriteRice(int value, int parameter)
    {
        uint folded = (uint)((value << 1) ^ (value >> 31));
        WriteUnary(folded >> parameter);
        Write(folded, parameter);
    }

    /// <summary>The UTF-8-like variable length integer used for frame numbers.</summary>
    public void WriteUtf8(ulong value)
    {
        if (value < 0x80)
        {
            Write((uint)value, 8);
            return;
        }

        int bytes = value < 0x800 ? 2 : value < 0x10000 ? 3 : value < 0x200000 ? 4 : value < 0x4000000 ? 5 : value < 0x80000000 ? 6 : 7;
        int shift = (bytes - 1) * 6;
        uint lead = (uint)(0xFF00 >> bytes) & 0xFF;
        Write(lead | (uint)(value >> shift), 8);
        while (shift > 0)
        {
            shift -= 6;
            Write(0x80 | (uint)((value >> shift) & 0x3F), 8);
        }
    }

    public void PadToByte()
    {
        if (_bits > 0) Write(0, 8 - _bits);
    }

    private void Put(byte value)
    {
        if (_bytes == _buffer.Length) Array.Resize(ref _buffer, _buffer.Length * 2);
        _buffer[_bytes++] = value;
    }
}
