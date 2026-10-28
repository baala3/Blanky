namespace Blanky.Core.Audio;

/// <summary>
/// Loudness matching, so every sound (built-in or imported) sits at a comparable
/// level. Same targets as Blankie: -27 LUFS, at most +18 dB of boost, and sounds
/// quieter than -60 LUFS are treated as silence and left alone.
/// </summary>
public static class Loudness
{
    public const double TargetLufs = -27;
    public const double MaxGainDb = 18;
    public const double MinimumLufs = -60;

    /// <summary>Linear gain that moves <paramref name="lufs"/> to the target. Boost is capped, cuts are not.</summary>
    public static float NormalizationFactor(double lufs)
    {
        if (double.IsNaN(lufs) || lufs <= MinimumLufs)
        {
            return 1f;
        }
        double gainDb = Math.Min(TargetLufs - lufs, MaxGainDb);
        return (float)Math.Pow(10, gainDb / 20);
    }

    /// <summary>Slider position (0..1) to linear gain. Cubic, like Blankie, so the slider feels even.</summary>
    public static float VolumeCurve(double slider)
    {
        double v = Math.Clamp(slider, 0, 1);
        return (float)(v * v * v);
    }

    /// <summary>
    /// Integrated loudness (ITU-R BS.1770-4) of 48 kHz interleaved stereo, read from
    /// <paramref name="decoder"/> until end of stream or <paramref name="maxDuration"/>.
    /// </summary>
    public static double MeasureIntegratedLufs(IAudioDecoder decoder, TimeSpan? maxDuration = null, CancellationToken cancellationToken = default)
    {
        var meter = new LoudnessMeter();
        long remaining = maxDuration is { } max ? (long)(max.TotalSeconds * Mixer.SampleRate) * 2 : long.MaxValue;
        var buffer = new float[Mixer.SampleRate];
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = decoder.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, remaining)));
            if (read == 0)
            {
                break;
            }
            meter.Add(buffer.AsSpan(0, read));
            remaining -= read;
        }
        return meter.IntegratedLufs;
    }
}

/// <summary>Streaming BS.1770 meter: K-weighting, 400 ms blocks with 75% overlap, absolute and relative gates.</summary>
internal sealed class LoudnessMeter
{
    private const int BlockFrames = Mixer.SampleRate * 400 / 1000;
    private const int StepFrames = BlockFrames / 4;

    // K-weighting at 48 kHz from the standard: a high-shelf pre-filter, then the RLB high-pass.
    private readonly Biquad[] _left = [Biquad.Shelf48k(), Biquad.HighPass48k()];
    private readonly Biquad[] _right = [Biquad.Shelf48k(), Biquad.HighPass48k()];
    private readonly double[] _stepEnergy = new double[4];
    private readonly List<double> _blocks = [];
    private double _energy;
    private int _framesInStep;
    private int _stepsSeen;

    public void Add(ReadOnlySpan<float> stereo)
    {
        for (int i = 0; i + 1 < stereo.Length; i += 2)
        {
            double l = _left[1].Process(_left[0].Process(stereo[i]));
            double r = _right[1].Process(_right[0].Process(stereo[i + 1]));
            _energy += (l * l) + (r * r);
            if (++_framesInStep == StepFrames)
            {
                _stepEnergy[_stepsSeen % 4] = _energy;
                _stepsSeen++;
                _energy = 0;
                _framesInStep = 0;
                if (_stepsSeen >= 4)
                {
                    _blocks.Add((_stepEnergy[0] + _stepEnergy[1] + _stepEnergy[2] + _stepEnergy[3]) / BlockFrames);
                }
            }
        }
    }

    public double IntegratedLufs
    {
        get
        {
            var absolute = _blocks.Where(z => BlockLoudness(z) > -70).ToList();
            if (absolute.Count == 0)
            {
                return double.NegativeInfinity;
            }
            double relativeGate = BlockLoudness(absolute.Average()) - 10;
            var gated = absolute.Where(z => BlockLoudness(z) > relativeGate).ToList();
            return gated.Count == 0 ? double.NegativeInfinity : BlockLoudness(gated.Average());
        }
    }

    private static double BlockLoudness(double meanSquare) => -0.691 + (10 * Math.Log10(meanSquare));

    private struct Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double _z1;
        private double _z2;

        public static Biquad Shelf48k() => new(1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585);

        public static Biquad HighPass48k() => new(1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);

        // Transposed direct form II.
        public double Process(double x)
        {
            double y = (b0 * x) + _z1;
            _z1 = (b1 * x) - (a1 * y) + _z2;
            _z2 = (b2 * x) - (a2 * y);
            return y;
        }
    }
}
