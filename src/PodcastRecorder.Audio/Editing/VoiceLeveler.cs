namespace PodcastRecorder.Audio.Editing;

/// <summary>
/// Evens out a recording's level over time: passages quieter than the recording as a whole are
/// raised and louder ones are lowered, slowly (over seconds), so a speaker who drifts from the
/// microphone or a quieter guest ends up as loud as the rest. Pauses are not pulled up: the gain
/// only follows stretches with speech in them and holds through the gaps.
/// </summary>
public sealed class VoiceLeveler
{
    /// <summary>The level is measured over blocks this long.</summary>
    public const int BlockFrames = AudioFormat.SampleRate / 10;

    /// <summary>Blocks on each side that make up a passage (3 s in all).</summary>
    private const int WindowBlocks = 15;

    /// <summary>Blocks on each side the gain is smoothed over (1 s in all).</summary>
    private const int SmoothBlocks = 5;

    /// <summary>Blocks this far under the recording's level are pauses, not speech.</summary>
    private const double GateDb = -20;

    private const double FloorDb = -50;

    private readonly float[] _gains;

    private VoiceLeveler(float[] gains) => _gains = gains;

    /// <summary>The gain of each block, in dB; for tests and curiosity.</summary>
    public IReadOnlyList<double> GainsDb => _gains.Select(g => 20 * Math.Log10(g)).ToList();

    /// <summary>Collects the level of a recording as it streams by.</summary>
    public sealed class Analyzer
    {
        private readonly List<double> _energies = [];
        private double _sum;
        private int _frames;

        /// <param name="samples">Interleaved stereo.</param>
        public void Add(ReadOnlySpan<float> samples)
        {
            for (int i = 0; i + 1 < samples.Length; i += 2)
            {
                _sum += (samples[i] * (double)samples[i] + samples[i + 1] * (double)samples[i + 1]) / 2;
                if (++_frames == BlockFrames) Complete();
            }
        }

        /// <param name="maximumDb">The most a passage is raised or lowered.</param>
        public VoiceLeveler Build(double maximumDb = 12)
        {
            if (_frames > BlockFrames / 2) Complete();
            return new VoiceLeveler(Gains(_energies, maximumDb));
        }

        private void Complete()
        {
            _energies.Add(_sum / _frames);
            _sum = 0;
            _frames = 0;
        }
    }

    /// <summary>Applies the gain to interleaved stereo samples that start at <paramref name="frame"/> of the recording.</summary>
    public void Apply(Span<float> samples, long frame)
    {
        if (_gains.Length == 0) return;
        for (int i = 0; i + 1 < samples.Length; i += 2, frame++)
        {
            // Gains sit at the middle of their blocks; in between they are interpolated.
            double at = (frame - BlockFrames / 2.0) / BlockFrames;
            int index = (int)Math.Floor(at);
            float a = _gains[Math.Clamp(index, 0, _gains.Length - 1)];
            float b = _gains[Math.Clamp(index + 1, 0, _gains.Length - 1)];
            float gain = a + (b - a) * (float)(at - index);
            samples[i] *= gain;
            samples[i + 1] *= gain;
        }
    }

    private static float[] Gains(List<double> energies, double maximumDb)
    {
        int count = energies.Count;
        var gains = new float[count];
        Array.Fill(gains, 1f);

        var audible = energies.Where(e => ToDb(e) > FloorDb).ToList();
        if (audible.Count == 0) return gains;
        double gate = ToDb(audible.Average()) + GateDb;
        var speech = energies.Select(e => ToDb(e) > gate).ToArray();
        // The level every passage is brought to: that of the speech as a whole.
        double reference = ToDb(energies.Where((_, i) => speech[i]).Average());

        var wanted = new double[count];
        double last = double.NaN;
        for (int i = 0; i < count; i++)
        {
            double sum = 0;
            int found = 0;
            for (int k = Math.Max(0, i - WindowBlocks); k <= Math.Min(count - 1, i + WindowBlocks); k++)
            {
                if (!speech[k]) continue;
                sum += energies[k];
                found++;
            }

            // Too little speech around here to judge: hold the gain.
            if (found >= 5) last = Math.Clamp(reference - ToDb(sum / found), -maximumDb, maximumDb);
            wanted[i] = last;
        }

        // Before the first passage: its gain.
        double first = wanted.FirstOrDefault(w => !double.IsNaN(w), 0);
        for (int i = 0; i < count; i++)
        {
            if (double.IsNaN(wanted[i])) wanted[i] = double.IsNaN(first) ? 0 : first;
        }

        for (int i = 0; i < count; i++)
        {
            double sum = 0;
            int from = Math.Max(0, i - SmoothBlocks);
            int to = Math.Min(count - 1, i + SmoothBlocks);
            for (int k = from; k <= to; k++) sum += wanted[k];
            gains[i] = (float)Math.Pow(10, sum / (to - from + 1) / 20);
        }

        return gains;
    }

    private static double ToDb(double meanSquare) => meanSquare <= 0 ? double.NegativeInfinity : 10 * Math.Log10(meanSquare);
}
