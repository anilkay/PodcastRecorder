using System.Buffers.Binary;

namespace PodcastRecorder.Audio.Recording;

internal static class PcmConversion
{
    /// <summary>Clamps float samples to [-1, 1] and writes them as little-endian 16-bit PCM.</summary>
    public static int ToPcm16(ReadOnlySpan<float> samples, ref byte[] target)
    {
        int bytes = samples.Length * 2;
        if (target.Length < bytes)
        {
            target = new byte[bytes];
        }

        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Math.Clamp(samples[i], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(target.AsSpan(i * 2), (short)(clamped * short.MaxValue));
        }

        return bytes;
    }
}
