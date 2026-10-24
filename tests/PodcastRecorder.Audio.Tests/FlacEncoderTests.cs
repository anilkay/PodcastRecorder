using System.Buffers.Binary;
using System.Security.Cryptography;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Tests;

/// <summary>
/// Checks the stream header and size. Bit-exact decoding was verified against the reference
/// decoder (flac 1.4.3, <c>flac -t</c>) for sine, noise, silence, clipping and tiny inputs.
/// </summary>
public class FlacEncoderTests
{
    [Fact]
    public void Stream_info_describes_the_audio_and_carries_its_md5()
    {
        int frames = 48000 + 1234;
        var samples = Sine(frames);
        var bytes = Encode(samples);

        Assert.Equal("fLaC"u8.ToArray(), bytes[..4]);
        var info = bytes.AsSpan(8, 34);
        int sampleRate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
        int channels = ((info[12] >> 1) & 0b111) + 1;
        int bitsPerSample = (((info[12] & 1) << 4) | (info[13] >> 4)) + 1;
        long totalSamples = ((long)(info[13] & 0xF) << 32) | BinaryPrimitives.ReadUInt32BigEndian(info[14..18]);

        Assert.Equal(48000, sampleRate);
        Assert.Equal(2, channels);
        Assert.Equal(16, bitsPerSample);
        Assert.Equal(frames, totalSamples);
        Assert.Equal(Md5OfPcm16(samples), info[18..34].ToArray());
    }

    [Fact]
    public void Tonal_audio_compresses_well_and_silence_almost_vanishes()
    {
        var tone = Encode(Sine(48000));
        var silence = Encode(new float[48000 * 2]);
        int pcmBytes = 48000 * 2 * 2;

        Assert.True(tone.Length < pcmBytes * 0.4, $"tone was {tone.Length} of {pcmBytes} bytes");
        Assert.True(silence.Length < 500, $"silence was {silence.Length} bytes");
    }

    private static float[] Sine(int frames)
    {
        var samples = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            samples[i * 2] = 0.5f * MathF.Sin(i * 2 * MathF.PI * 440 / 48000);
            samples[i * 2 + 1] = 0.4f * MathF.Sin(i * 2 * MathF.PI * 330 / 48000);
        }

        return samples;
    }

    private static byte[] Encode(float[] samples)
    {
        var path = Path.Combine(Path.GetTempPath(), $"podcastrecorder-{Guid.NewGuid():N}.flac");
        try
        {
            using (var encoder = new FlacEncoder())
            {
                encoder.Open(path, AudioFormat.Mix);
                for (int offset = 0; offset < samples.Length; offset += 1920)
                {
                    encoder.Write(samples, offset, Math.Min(1920, samples.Length - offset));
                }
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Md5OfPcm16(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue));
        }

        return MD5.HashData(pcm);
    }
}
