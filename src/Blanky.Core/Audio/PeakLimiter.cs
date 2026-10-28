namespace Blanky.Core.Audio;

/// <summary>
/// Mix-bus peak limiter. Instant attack keeps every sample under the ceiling, and a
/// slow release lets gain recover without pumping. Several loud sounds summed (or
/// a quiet import boosted by normalization) never clip.
/// </summary>
internal sealed class PeakLimiter
{
    private readonly float _ceiling;
    private readonly float _release;
    private float _gain = 1f;

    /// <param name="ceilingDb">Output ceiling in dBFS.</param>
    /// <param name="releaseSeconds">Time constant for gain recovery.</param>
    /// <param name="sampleRate">Frames per second.</param>
    public PeakLimiter(float ceilingDb = -1f, float releaseSeconds = 0.25f, int sampleRate = Mixer.SampleRate)
    {
        _ceiling = MathF.Pow(10, ceilingDb / 20);
        _release = 1f - MathF.Exp(-1f / (releaseSeconds * sampleRate));
    }

    /// <summary>Limits interleaved stereo samples in place, linking both channels.</summary>
    public void Process(Span<float> stereo)
    {
        float gain = _gain;
        for (int i = 0; i + 1 < stereo.Length; i += 2)
        {
            float peak = MathF.Max(MathF.Abs(stereo[i]), MathF.Abs(stereo[i + 1]));
            gain += (1f - gain) * _release;
            if (peak * gain > _ceiling)
            {
                gain = _ceiling / peak;
            }
            stereo[i] *= gain;
            stereo[i + 1] *= gain;
        }
        _gain = gain;
    }
}
