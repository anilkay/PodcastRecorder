using NAudio.Wave;

namespace PodcastRecorder.Audio.Editing;

/// <summary>
/// Streams a source with the cuts left out. Every cut inside the recording is joined with a short
/// equal-power crossfade centred on the cut, made of audio that was going to be removed anyway,
/// so the result is exactly the source length minus the cuts and every file given the same cut
/// list (the mix and its tracks) comes out the same length. A cut at the very start fades in
/// what follows it; one at the end fades out what comes before it.
/// </summary>
internal static class CutRenderer
{
    private const int BlockFrames = 8192;

    /// <param name="totalFrames">Length of the source in frames; cuts are clamped to it.</param>
    /// <param name="write">Receives interleaved samples in the source format.</param>
    /// <returns>The number of frames written.</returns>
    public static long Render(
        ISampleProvider source, IReadOnlyList<TimeRange> cuts, TimeSpan crossfade, long totalFrames,
        Action<float[], int, int> write, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        int rate = source.WaveFormat.SampleRate;
        int channels = source.WaveFormat.Channels;
        long fadeFrames = Math.Max(0, CutList.ToFrame(crossfade, rate));
        var spans = Plan(CutList.ToFrames(cuts, rate, totalFrames), fadeFrames, totalFrames);

        var input = new float[BlockFrames * channels];
        var output = new Output(write, BlockFrames * channels);
        int halfMax = (int)Math.Max(1, fadeFrames / 2);
        var tailA = new float[halfMax * 2 * channels];
        var headB = new float[halfMax * 2 * channels];
        int next = 0;
        long position = 0;
        int read;

        while ((read = source.Read(input, 0, input.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = read / channels;
            for (int f = 0; f < frames; f++, position++)
            {
                while (next < spans.Count && position >= spans[next].WindowEnd)
                {
                    spans[next].Finish(tailA, headB, channels, output);
                    next++;
                }

                var frame = input.AsSpan(f * channels, channels);
                if (next >= spans.Count || position < spans[next].WindowStart)
                {
                    output.Add(frame, 1f);
                    continue;
                }

                spans[next].Take(position, frame, tailA, headB, channels, output);
            }

            if (totalFrames > 0) progress?.Report(Math.Min((double)position / totalFrames, 1));
        }

        // The source ran out early (its length was an estimate): keep what came before the cut.
        if (next < spans.Count) spans[next].Truncate(position, tailA, channels, output);

        output.Flush();
        return output.Written;
    }

    /// <summary>
    /// Merges cuts that leave less than one crossfade between them, then sizes each crossfade so
    /// it never reaches past half of the audio kept on either side.
    /// </summary>
    internal static List<CutSpan> Plan(IReadOnlyList<(long Start, long End)> cuts, long fadeFrames, long totalFrames)
    {
        var merged = new List<(long Start, long End)>();
        foreach (var cut in cuts)
        {
            if (merged.Count > 0 && cut.Start - merged[^1].End < Math.Max(fadeFrames, 1))
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, cut.End));
            }
            else
            {
                merged.Add(cut);
            }
        }

        var spans = new List<CutSpan>(merged.Count);
        for (int i = 0; i < merged.Count; i++)
        {
            var (a, b) = merged[i];
            long keptBefore = a - (i > 0 ? merged[i - 1].End : 0);
            long keptAfter = (i + 1 < merged.Count ? merged[i + 1].Start : totalFrames) - b;
            if (a == 0)
            {
                spans.Add(new CutSpan(SpanKind.AtStart, a, b, Math.Min(fadeFrames, keptAfter / 2)));
            }
            else if (b >= totalFrames)
            {
                spans.Add(new CutSpan(SpanKind.AtEnd, a, b, Math.Min(fadeFrames, keptBefore / 2)));
            }
            else
            {
                long half = Math.Min(fadeFrames / 2, Math.Min(keptBefore, keptAfter) / 2);
                spans.Add(new CutSpan(SpanKind.Inside, a, b, half));
            }
        }

        return spans;
    }

    internal enum SpanKind
    {
        AtStart,
        Inside,
        AtEnd,
    }

    /// <summary>
    /// One cut from frame <see cref="A"/> to <see cref="B"/>. Inside the recording the crossfade
    /// mixes [A-h, A+h) fading out with [B-h, B+h) fading in, h being <see cref="Fade"/>.
    /// </summary>
    internal sealed class CutSpan(SpanKind kind, long a, long b, long fade)
    {
        public SpanKind Kind => kind;

        public long A => a;

        public long B => b;

        public long Fade => fade;

        public long WindowStart => kind switch
        {
            SpanKind.AtStart => 0,
            SpanKind.Inside => a - fade,
            _ => a - fade,
        };

        public long WindowEnd => kind switch
        {
            SpanKind.AtStart => b + fade,
            SpanKind.Inside => b + fade,
            _ => long.MaxValue,
        };

        public void Take(long position, Span<float> frame, float[] tailA, float[] headB, int channels, Output output)
        {
            switch (kind)
            {
                case SpanKind.AtStart:
                    if (position >= b) output.Add(frame, Ramp(position - b, fade, rising: true));
                    break;
                case SpanKind.AtEnd:
                    if (position < a) output.Add(frame, Ramp(position - (a - fade), fade, rising: false));
                    break;
                default:
                    if (position >= a - fade && position < a + fade) frame.CopyTo(tailA.AsSpan((int)(position - (a - fade)) * channels));
                    if (position >= b - fade && position < b + fade) frame.CopyTo(headB.AsSpan((int)(position - (b - fade)) * channels));
                    break;
            }
        }

        /// <summary>Called once the source has passed <see cref="WindowEnd"/>: writes the crossfade.</summary>
        public void Finish(float[] tailA, float[] headB, int channels, Output output)
        {
            if (kind != SpanKind.Inside) return;

            long length = fade * 2;
            Span<float> mixed = stackalloc float[channels];
            for (long k = 0; k < length; k++)
            {
                double t = (k + 0.5) / length * Math.PI / 2;
                float fadeOut = (float)Math.Cos(t);
                float fadeIn = (float)Math.Sin(t);
                for (int c = 0; c < channels; c++)
                {
                    mixed[c] = tailA[k * channels + c] * fadeOut + headB[k * channels + c] * fadeIn;
                }

                output.Add(mixed, 1f);
            }
        }

        /// <summary>The source ended at <paramref name="end"/> inside this cut's window.</summary>
        public void Truncate(long end, float[] tailA, int channels, Output output)
        {
            if (kind != SpanKind.Inside) return;

            long kept = Math.Min(end, a) - (a - fade);
            for (long k = 0; k < kept; k++)
            {
                output.Add(tailA.AsSpan((int)k * channels, channels), Ramp(k, fade, rising: false));
            }
        }

        /// <summary>Equal-power gain at step <paramref name="k"/> of a fade <paramref name="length"/> frames long.</summary>
        private static float Ramp(long k, long length, bool rising)
        {
            if (length <= 0) return 1f;
            double t = (k + 0.5) / length * Math.PI / 2;
            return (float)(rising ? Math.Sin(t) : Math.Cos(t));
        }
    }

    /// <summary>Collects frames and hands them on in blocks.</summary>
    internal sealed class Output(Action<float[], int, int> write, int capacity)
    {
        private readonly float[] _buffer = new float[capacity];
        private int _count;

        public long Written { get; private set; }

        public void Add(ReadOnlySpan<float> frame, float gain)
        {
            if (_count + frame.Length > _buffer.Length) Flush();
            for (int c = 0; c < frame.Length; c++) _buffer[_count + c] = frame[c] * gain;
            _count += frame.Length;
            Written++;
        }

        public void Flush()
        {
            if (_count == 0) return;
            write(_buffer, 0, _count);
            _count = 0;
        }
    }
}
