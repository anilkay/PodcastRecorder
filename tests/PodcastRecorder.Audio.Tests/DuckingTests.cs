using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.Audio.Tests;

public class DuckingTests
{
    private const int OneSecond = AudioFormat.SampleRate * AudioFormat.Channels;

    [Fact]
    public void Fade_reaches_its_target_after_the_set_time()
    {
        var channel = new MixerChannel("music", new ConstantSource(1f));
        channel.FadeTo(0.25f, TimeSpan.FromMilliseconds(100));
        var buffer = new float[OneSecond / 10];

        channel.Read(buffer, 0, buffer.Length);
        Assert.True(buffer[0] > 0.9f, "the fade should start near the old level");
        Assert.Equal(0.25f, buffer[^1], 3);

        channel.Read(buffer, 0, buffer.Length);
        Assert.All(buffer, s => Assert.Equal(0.25f, s, 5));
    }

    [Fact]
    public void Live_microphone_ducks_music_but_not_jingles()
    {
        using var engine = new AudioEngine();
        engine.DuckLevelDb = -6;
        engine.AddMicrophone(new ConstantSource(0f), "Mikrofon");

        Assert.Equal(0.501f, engine.MusicChannel.FadeTarget, 3);
        Assert.Equal(1f, engine.JingleChannel.FadeTarget);

        engine.IsMicrophoneLive = false;
        Assert.Equal(1f, engine.MusicChannel.FadeTarget);
    }

    [Fact]
    public void Ducking_can_be_turned_off()
    {
        using var engine = new AudioEngine();
        engine.AddMicrophone(new ConstantSource(0f), "Mikrofon");

        engine.DuckingEnabled = false;

        Assert.Equal(1f, engine.MusicChannel.FadeTarget);
    }

    [Fact]
    public void Microphone_off_removes_it_from_the_recording()
    {
        using var engine = new AudioEngine();
        engine.IsMicrophoneLive = false;
        engine.AddMicrophone(new ConstantSource(0.5f), "Mikrofon");
        var program = new float[960];

        engine.Mixer.Mix(program, new float[960]);

        Assert.All(program, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void No_ducking_without_a_microphone()
    {
        using var engine = new AudioEngine();

        Assert.Equal(1f, engine.MusicChannel.FadeTarget);
    }

    [Fact]
    public void Voice_ducking_follows_speech_and_releases_after_the_hold()
    {
        using var engine = new AudioEngine();
        var voice = new SwitchableSource();
        engine.AddMicrophone(voice, "Mikrofon");
        engine.DuckingMode = DuckingMode.WhileSpeaking;
        engine.VoiceHoldTime = TimeSpan.FromMilliseconds(100);
        var buffer = new float[960];

        engine.Bus.Read(buffer, 0, buffer.Length);
        Assert.Equal(1f, engine.MusicChannel.FadeTarget);

        voice.Level = 0.3f;
        engine.Bus.Read(buffer, 0, buffer.Length);
        Assert.True(engine.IsVoiceDetected);
        Assert.True(engine.MusicChannel.FadeTarget < 1f);

        voice.Level = 0f;
        for (int i = 0; i < 12; i++) engine.Bus.Read(buffer, 0, buffer.Length);
        Assert.False(engine.IsVoiceDetected);
        Assert.Equal(1f, engine.MusicChannel.FadeTarget);
    }

    private sealed class SwitchableSource : NAudio.Wave.ISampleProvider
    {
        public float Level { get; set; }

        public NAudio.Wave.WaveFormat WaveFormat => AudioFormat.Mix;

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, Level, offset, count);
            return count;
        }
    }
}
