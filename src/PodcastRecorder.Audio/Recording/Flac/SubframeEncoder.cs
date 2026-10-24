namespace PodcastRecorder.Audio.Recording.Flac;

/// <summary>
/// Plans and writes one FLAC subframe: CONSTANT for silence, otherwise the best FIXED
/// predictor (order 0-4) with partitioned Rice coding, falling back to VERBATIM.
/// </summary>
internal sealed class SubframeEncoder
{
    private const int MaxFixedOrder = 4;
    private const int MaxPartitionOrder = 8;
    private const int MaxRiceParameter = 14;

    private int[] _residual = [];
    private uint[] _folded = [];
    private readonly int[] _riceParameters = new int[1 << MaxPartitionOrder];
    private readonly int[] _bestRiceParameters = new int[1 << MaxPartitionOrder];

    private int[] _samples = [];
    private int _count;
    private int _bitsPerSample;
    private SubframeKind _kind;
    private int _order;
    private int _partitionOrder;

    /// <summary>Size in bits of the planned subframe.</summary>
    public long Bits { get; private set; }

    public void Plan(int[] samples, int count, int bitsPerSample)
    {
        _samples = samples;
        _count = count;
        _bitsPerSample = bitsPerSample;
        if (_residual.Length < count)
        {
            _residual = new int[count];
            _folded = new uint[count];
        }

        if (IsConstant())
        {
            _kind = SubframeKind.Constant;
            Bits = 8 + bitsPerSample;
            return;
        }

        _order = BestFixedOrder();
        ComputeResidual(_order);
        long residualBits = PlanRice();
        long fixedBits = 8 + (long)_order * bitsPerSample + residualBits;
        long verbatimBits = 8 + (long)count * bitsPerSample;

        _kind = fixedBits < verbatimBits ? SubframeKind.Fixed : SubframeKind.Verbatim;
        Bits = Math.Min(fixedBits, verbatimBits);
    }

    public void Write(BitWriter writer)
    {
        switch (_kind)
        {
            case SubframeKind.Constant:
                writer.Write(0b0_000000_0, 8);
                writer.WriteSigned(_samples[0], _bitsPerSample);
                break;

            case SubframeKind.Verbatim:
                writer.Write(0b0_000001_0, 8);
                for (int i = 0; i < _count; i++) writer.WriteSigned(_samples[i], _bitsPerSample);
                break;

            case SubframeKind.Fixed:
                writer.Write((uint)(0b0_001000_0 | (_order << 1)), 8);
                for (int i = 0; i < _order; i++) writer.WriteSigned(_samples[i], _bitsPerSample);
                WriteResidual(writer);
                break;
        }
    }

    private bool IsConstant()
    {
        int first = _samples[0];
        for (int i = 1; i < _count; i++)
        {
            if (_samples[i] != first) return false;
        }

        return true;
    }

    private int BestFixedOrder()
    {
        int maxOrder = Math.Min(MaxFixedOrder, _count - 1);
        int best = 0;
        long bestSum = long.MaxValue;
        for (int order = 0; order <= maxOrder; order++)
        {
            long sum = 0;
            for (int i = order; i < _count; i++) sum += Math.Abs(Predict(order, i));
            if (sum < bestSum)
            {
                bestSum = sum;
                best = order;
            }
        }

        return best;
    }

    private long Predict(int order, int i)
    {
        var x = _samples;
        return order switch
        {
            0 => x[i],
            1 => (long)x[i] - x[i - 1],
            2 => (long)x[i] - 2L * x[i - 1] + x[i - 2],
            3 => (long)x[i] - 3L * x[i - 1] + 3L * x[i - 2] - x[i - 3],
            _ => (long)x[i] - 4L * x[i - 1] + 6L * x[i - 2] - 4L * x[i - 3] + x[i - 4],
        };
    }

    private void ComputeResidual(int order)
    {
        for (int i = order; i < _count; i++)
        {
            int r = (int)Predict(order, i);
            _residual[i] = r;
            _folded[i] = (uint)((r << 1) ^ (r >> 31));
        }
    }

    /// <summary>Picks the partition order and Rice parameters with the fewest bits.</summary>
    private long PlanRice()
    {
        long bestBits = long.MaxValue;
        for (int p = 0; p <= MaxPartitionOrder; p++)
        {
            int partitions = 1 << p;
            if (_count % partitions != 0 || (_count >> p) <= _order) break;

            long bits = 2 + 4;
            int size = _count >> p;
            for (int part = 0; part < partitions; part++)
            {
                int start = part == 0 ? _order : part * size;
                int end = (part + 1) * size;
                bits += 4 + BestRiceBits(start, end, out _riceParameters[part]);
            }

            if (bits < bestBits)
            {
                bestBits = bits;
                _partitionOrder = p;
                Array.Copy(_riceParameters, _bestRiceParameters, partitions);
            }
        }

        return bestBits;
    }

    private long BestRiceBits(int start, int end, out int parameter)
    {
        int count = end - start;
        if (count == 0)
        {
            parameter = 0;
            return 0;
        }

        ulong sum = 0;
        for (int i = start; i < end; i++) sum += _folded[i];
        double mean = (double)sum / count;
        int guess = mean < 1 ? 0 : Math.Min(MaxRiceParameter, (int)Math.Floor(Math.Log2(mean)));

        long best = long.MaxValue;
        parameter = 0;
        for (int k = Math.Max(0, guess - 1); k <= Math.Min(MaxRiceParameter, guess + 1); k++)
        {
            long bits = (long)count * (k + 1);
            for (int i = start; i < end; i++) bits += _folded[i] >> k;
            if (bits < best)
            {
                best = bits;
                parameter = k;
            }
        }

        return best;
    }

    private void WriteResidual(BitWriter writer)
    {
        writer.Write(0b00, 2);
        writer.Write((uint)_partitionOrder, 4);
        int partitions = 1 << _partitionOrder;
        int size = _count >> _partitionOrder;
        for (int part = 0; part < partitions; part++)
        {
            int k = _bestRiceParameters[part];
            writer.Write((uint)k, 4);
            int start = part == 0 ? _order : part * size;
            int end = (part + 1) * size;
            for (int i = start; i < end; i++) writer.WriteRice(_residual[i], k);
        }
    }

    private enum SubframeKind
    {
        Constant,
        Verbatim,
        Fixed,
    }
}
