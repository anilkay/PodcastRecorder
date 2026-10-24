using NAudio.Wave;
using PodcastRecorder.Audio.Recording;

namespace PodcastRecorder.Audio.Mixing;

/// <summary>
/// The provider the output device pulls from: mixes both buses, sends the program bus to the
/// recorder and returns the monitor bus for playback.
/// </summary>
/// <param name="afterMix">Called on the audio thread after each block, with its frame count.</param>
public sealed class OutputBus(AudioMixer mixer, Recorder recorder, Action<int>? afterMix = null) : ISampleProvider
{
    private readonly ChannelSink _stemSink = recorder.WriteStem;
    private float[] _program = [];
    private float[] _monitor = [];

    public WaveFormat WaveFormat => AudioFormat.Mix;

    /// <summary>Loudness of the program bus; the integrated value counts recorded audio only.</summary>
    public LoudnessMeter Loudness { get; } = new();

    public int Read(float[] buffer, int offset, int count)
    {
        if (_program.Length < count)
        {
            _program = new float[count];
            _monitor = new float[count];
        }

        var program = _program.AsSpan(0, count);
        var monitor = _monitor.AsSpan(0, count);
        mixer.Mix(program, monitor, recorder.IsRecordingStems ? _stemSink : null);
        recorder.Write(program);
        Loudness.Process(program, integrate: recorder.State == RecorderState.Recording);
        afterMix?.Invoke(count / AudioFormat.Channels);
        monitor.CopyTo(buffer.AsSpan(offset, count));
        return count;
    }
}
