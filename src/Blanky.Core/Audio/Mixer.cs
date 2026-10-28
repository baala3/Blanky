using NAudio.Wave;

namespace Blanky.Core.Audio;

/// <summary>
/// The render graph: sums every voice, applies the smoothed master volume, then
/// the peak limiter. Pulled by the output on the audio thread.
/// </summary>
internal sealed class Mixer : ISampleProvider
{
    public const int SampleRate = 48_000;

    private static readonly float MasterSmoothing = 1f - MathF.Exp(-1f / (0.02f * SampleRate));

    private readonly PeakLimiter _limiter = new();
    private readonly Lock _writeLock = new();
    private float[] _scratch = new float[SampleRate];
    private volatile Voice[] _voices = [];
    private volatile float _targetMaster = 1f;
    private float _master = 1f;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    /// <summary>Snapshot of the voices being mixed. Replaced wholesale, never mutated.</summary>
    public IReadOnlyList<Voice> Voices => _voices;

    public float MasterVolume
    {
        get => _targetMaster;
        set => _targetMaster = Math.Clamp(value, 0f, 1f);
    }

    // Copy-on-write, so the audio thread iterates a stable array without locking.
    // Writers (UI and decode threads) serialize on the lock.
    public void Add(Voice voice)
    {
        lock (_writeLock)
        {
            _voices = [.. _voices, voice];
        }
    }

    public void Remove(Voice voice)
    {
        lock (_writeLock)
        {
            _voices = [.. _voices.Where(v => v != voice)];
        }
    }

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        if (_scratch.Length < buffer.Length)
        {
            _scratch = new float[buffer.Length];
        }

        foreach (var voice in _voices)
        {
            voice.MixInto(buffer, _scratch);
        }

        float target = _targetMaster;
        float master = _master;
        for (int i = 0; i + 1 < buffer.Length; i += 2)
        {
            master += (target - master) * MasterSmoothing;
            buffer[i] *= master;
            buffer[i + 1] *= master;
        }
        _master = master;

        _limiter.Process(buffer);
        return buffer.Length;
    }
}
