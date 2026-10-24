using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PodcastRecorder.Audio.Sources;

public static class FormatConversion
{
    private static readonly Guid IeeeFloatSubType = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// Converts any float sample stream to <see cref="AudioFormat.Mix"/>: mono is duplicated,
    /// more than two channels keep the first two, and the rate is resampled to 48 kHz.
    /// </summary>
    public static ISampleProvider ToMixFormat(ISampleProvider source)
    {
        ISampleProvider result = source.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(source),
            2 => source,
            _ => new FirstTwoChannelsSampleProvider(source),
        };

        if (result.WaveFormat.SampleRate != AudioFormat.SampleRate)
        {
            result = new WdlResamplingSampleProvider(result, AudioFormat.SampleRate);
        }

        return result;
    }

    /// <summary>
    /// WASAPI reports its mix format as WAVE_FORMAT_EXTENSIBLE. The bytes are plain PCM or float,
    /// so describe them that way to let NAudio's sample converters accept them.
    /// </summary>
    public static WaveFormat Simplify(WaveFormat format)
    {
        if (format is not WaveFormatExtensible extensible)
        {
            return format;
        }

        return extensible.SubFormat == IeeeFloatSubType
            ? WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels)
            : new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
    }

    private sealed class FirstTwoChannelsSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private readonly int _sourceChannels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            int needed = frames * _sourceChannels;
            if (_buffer.Length < needed)
            {
                _buffer = new float[needed];
            }

            int read = source.Read(_buffer, 0, needed);
            int framesRead = read / _sourceChannels;
            for (int f = 0; f < framesRead; f++)
            {
                buffer[offset + f * 2] = _buffer[f * _sourceChannels];
                buffer[offset + f * 2 + 1] = _buffer[f * _sourceChannels + 1];
            }

            return framesRead * 2;
        }
    }
}
