namespace PodcastRecorder.Audio.Mixing;

/// <summary>
/// Loudness per ITU-R BS.1770-4 / EBU R128: K-weighted, with momentary (400 ms),
/// short-term (3 s) and gated integrated values in LUFS. Podcasts usually aim for -16 LUFS.
/// </summary>
public sealed class LoudnessMeter
{
    private const int SubBlockFrames = AudioFormat.SampleRate / 10;
    private const int MomentarySubBlocks = 4;
    private const int ShortTermSubBlocks = 30;
    private const double AbsoluteGate = -70;
    private const double RelativeGate = -10;

    private readonly Lock _gate = new();
    private readonly KWeighting[] _filters = [new(), new()];
    private readonly double[] _subBlocks = new double[ShortTermSubBlocks];
    private readonly List<double> _gatingBlocks = [];
    private int _subBlockCount;
    private double _energy;
    private int _frames;

    public double MomentaryLufs
    {
        get
        {
            lock (_gate) return ToLufs(MeanOfLast(MomentarySubBlocks));
        }
    }

    public double ShortTermLufs
    {
        get
        {
            lock (_gate) return ToLufs(MeanOfLast(ShortTermSubBlocks));
        }
    }

    /// <summary>Integrated loudness of everything measured since <see cref="ResetIntegrated"/>.</summary>
    public double IntegratedLufs
    {
        get
        {
            lock (_gate)
            {
                var aboveAbsolute = _gatingBlocks.Where(e => ToLufs(e) > AbsoluteGate).ToList();
                if (aboveAbsolute.Count == 0) return double.NegativeInfinity;

                double threshold = ToLufs(aboveAbsolute.Average()) + RelativeGate;
                var gated = aboveAbsolute.Where(e => ToLufs(e) > threshold).ToList();
                return gated.Count == 0 ? double.NegativeInfinity : ToLufs(gated.Average());
            }
        }
    }

    public void ResetIntegrated()
    {
        lock (_gate) _gatingBlocks.Clear();
    }

    /// <param name="integrate">Whether this audio counts towards the integrated value (e.g. only while recording).</param>
    public void Process(ReadOnlySpan<float> samples, bool integrate)
    {
        for (int i = 0; i + 1 < samples.Length; i += 2)
        {
            double left = _filters[0].Process(samples[i]);
            double right = _filters[1].Process(samples[i + 1]);
            _energy += left * left + right * right;

            if (++_frames == SubBlockFrames)
            {
                lock (_gate) CompleteSubBlock(integrate);
            }
        }
    }

    private void CompleteSubBlock(bool integrate)
    {
        _subBlocks[_subBlockCount % ShortTermSubBlocks] = _energy / SubBlockFrames;
        _subBlockCount++;
        _energy = 0;
        _frames = 0;

        // Gating blocks are 400 ms long and start every 100 ms (75% overlap).
        if (integrate && _subBlockCount >= MomentarySubBlocks)
        {
            _gatingBlocks.Add(MeanOfLast(MomentarySubBlocks));
        }
    }

    private double MeanOfLast(int count)
    {
        int available = Math.Min(count, _subBlockCount);
        if (available == 0) return 0;

        double sum = 0;
        for (int i = 1; i <= available; i++) sum += _subBlocks[(_subBlockCount - i) % ShortTermSubBlocks];
        return sum / available;
    }

    private static double ToLufs(double meanSquare) =>
        meanSquare <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(meanSquare);

    /// <summary>The two-stage K-weighting filter at 48 kHz (BS.1770-4, table 1 and 2).</summary>
    private sealed class KWeighting
    {
        private readonly Biquad _shelf = new(1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585);
        private readonly Biquad _highPass = new(1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);

        public double Process(double x) => _highPass.Process(_shelf.Process(x));
    }

    private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double _z1;
        private double _z2;

        public double Process(double x)
        {
            double y = b0 * x + _z1;
            _z1 = b1 * x - a1 * y + _z2;
            _z2 = b2 * x - a2 * y;
            return y;
        }
    }
}
