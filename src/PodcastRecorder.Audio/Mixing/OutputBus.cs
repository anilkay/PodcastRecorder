using NAudio.Wave;

namespace PodcastRecorder.Audio.Mixing;

/// <summary>
/// The provider the output device pulls from: mixes both buses, sends the program bus to the
/// recorder and the live stream, and returns the monitor bus for playback.
/// </summary>
public sealed class OutputBus(AudioMixer mixer, params IReadOnlyList<IProgramSink> sinks) : ISampleProvider
{
    private float[] _program = [];
    private float[] _monitor = [];

    public WaveFormat WaveFormat => AudioFormat.Mix;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_program.Length < count)
        {
            _program = new float[count];
            _monitor = new float[count];
        }

        var program = _program.AsSpan(0, count);
        var monitor = _monitor.AsSpan(0, count);
        mixer.Mix(program, monitor);
        foreach (var sink in sinks) sink.Write(program);
        monitor.CopyTo(buffer.AsSpan(offset, count));
        return count;
    }
}
