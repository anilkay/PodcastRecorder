namespace PodcastRecorder.Audio.Recording.Flac;

internal static class FlacCrc
{
    private static readonly byte[] Crc8Table = BuildCrc8();
    private static readonly ushort[] Crc16Table = BuildCrc16();

    /// <summary>CRC-8, polynomial x^8 + x^2 + x + 1, over the frame header.</summary>
    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data) crc = Crc8Table[crc ^ b];
        return crc;
    }

    /// <summary>CRC-16, polynomial x^16 + x^15 + x^2 + 1, over the whole frame.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data) crc = (ushort)((crc << 8) ^ Crc16Table[(crc >> 8) ^ b]);
        return crc;
    }

    private static byte[] BuildCrc8()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            int crc = i;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1;
            table[i] = (byte)crc;
        }

        return table;
    }

    private static ushort[] BuildCrc16()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            int crc = i << 8;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1;
            table[i] = (ushort)crc;
        }

        return table;
    }
}
