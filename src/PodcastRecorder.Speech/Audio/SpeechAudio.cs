using System.Buffers.Binary;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PodcastRecorder.Speech.Audio;

/// <summary>A piece of a recording as a 16 kHz mono 16-bit WAV file in memory.</summary>
public sealed record AudioChunk(TimeSpan Offset, TimeSpan Duration, byte[] Wav);

/// <summary>Prepares recordings for speech recognition, which expects 16 kHz mono.</summary>
public static class SpeechAudio
{
    public const int SampleRate = 16000;

    private const int EnergyWindow = SampleRate / 10;

    /// <summary>
    /// Splits the audio into WAV chunks of about <paramref name="chunkLength"/>. Each cut is moved
    /// to the quietest tenth of a second in the last few seconds before it, so words are not split.
    /// </summary>
    public static IEnumerable<AudioChunk> ReadChunks(ISampleProvider source, TimeSpan chunkLength)
    {
        var speech = ToSpeechFormat(source);
        int target = Math.Max(EnergyWindow * 4, (int)(chunkLength.TotalSeconds * SampleRate));
        int search = Math.Min(SampleRate * 10, target / 4);
        var buffer = new float[target];
        int filled = 0;
        long emitted = 0;

        while (true)
        {
            filled += ReadFully(speech, buffer, filled, target - filled);
            if (filled < target)
            {
                if (filled > 0) yield return Chunk(emitted, buffer.AsSpan(0, filled));
                yield break;
            }

            int cut = QuietestPoint(buffer, target - search, target);
            yield return Chunk(emitted, buffer.AsSpan(0, cut));
            emitted += cut;
            Array.Copy(buffer, cut, buffer, 0, target - cut);
            filled = target - cut;
        }
    }

    /// <summary>Mixes all channels down to mono and resamples to 16 kHz.</summary>
    public static ISampleProvider ToSpeechFormat(ISampleProvider source)
    {
        var mono = source.WaveFormat.Channels == 1 ? source : new DownmixSampleProvider(source);
        return mono.WaveFormat.SampleRate == SampleRate ? mono : new WdlResamplingSampleProvider(mono, SampleRate);
    }

    /// <summary>A 16-bit PCM WAV file holding <paramref name="samples"/> at 16 kHz mono.</summary>
    public static byte[] ToWav(ReadOnlySpan<float> samples)
    {
        const int header = 44;
        var wav = new byte[header + samples.Length * 2];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], wav.Length - 8);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], samples.Length * 2);

        for (int i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(span[(header + i * 2)..], value);
        }

        return wav;
    }

    private static AudioChunk Chunk(long offsetSamples, ReadOnlySpan<float> samples) => new(
        TimeSpan.FromSeconds((double)offsetSamples / SampleRate),
        TimeSpan.FromSeconds((double)samples.Length / SampleRate),
        ToWav(samples));

    /// <summary>The middle of the quietest window between <paramref name="from"/> and <paramref name="to"/>.</summary>
    internal static int QuietestPoint(float[] samples, int from, int to)
    {
        int best = to;
        double bestEnergy = double.MaxValue;
        for (int start = from; start + EnergyWindow <= to; start += EnergyWindow / 2)
        {
            double energy = 0;
            for (int i = start; i < start + EnergyWindow; i++)
            {
                energy += samples[i] * samples[i];
            }

            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                best = start + EnergyWindow / 2;
            }
        }

        return best;
    }

    private static int ReadFully(ISampleProvider source, float[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = source.Read(buffer, offset + total, count - total);
            if (read == 0) break;
            total += read;
        }

        return total;
    }

    private sealed class DownmixSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int needed = count * _channels;
            if (_buffer.Length < needed)
            {
                _buffer = new float[needed];
            }

            int frames = source.Read(_buffer, 0, needed) / _channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++)
                {
                    sum += _buffer[f * _channels + c];
                }

                buffer[offset + f] = sum / _channels;
            }

            return frames;
        }
    }
}
