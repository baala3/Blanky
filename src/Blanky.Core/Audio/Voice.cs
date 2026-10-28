namespace Blanky.Core.Audio;

/// <summary>
/// One sound in the mix. The decode thread fills its ring from a
/// <see cref="LoopingSource"/>, and the audio thread drains it, applying a
/// smoothed gain and a linear play/pause fade.
/// </summary>
/// <remarks>
/// Threading: control setters are called from the UI thread and only publish
/// volatile targets. <see cref="Pump"/> and <see cref="DisposeSource"/> run on the
/// decode thread. <see cref="MixInto"/> runs on the audio thread. The ring is the
/// only state the decode and audio threads share.
/// </remarks>
internal sealed class Voice
{
    // About 0.75 s of stereo audio buffered ahead at 48 kHz.
    private const int RingCapacity = 72_000;
    private const int PumpChunk = 4_800;

    // One-pole smoothing for gain changes (20 ms time constant), so dragging a
    // slider never zippers.
    private static readonly float GainSmoothing = 1f - MathF.Exp(-1f / (0.02f * Mixer.SampleRate));

    private readonly SampleRingBuffer _ring = new(RingCapacity);
    private readonly LoopingSource _source;
    private readonly float[] _pumpBuffer = new float[PumpChunk];

    // Published by the UI thread.
    private volatile float _targetGain;
    private volatile FadeRequest _fadeRequest = new(0, 0, 0);

    // Audio-thread state.
    private float _gain;
    private float _fade;
    private float _fadeStep;
    private float _fadeTarget;
    private int _appliedFadeId;
    private long _framesPlayed;

    // Decode-thread state, published for the UI.
    private volatile bool _sourceEnded;

    public Voice(string key, LoopingSource source, long estimatedLoopFrames)
    {
        Key = key;
        _source = source;
        EstimatedLoopFrames = estimatedLoopFrames;
    }

    public string Key { get; }

    public long EstimatedLoopFrames { get; }

    /// <summary>Set by a stop: the decode thread drops the voice once its fade-out reaches silence.</summary>
    public bool RetireWhenSilent
    {
        get => Volatile.Read(ref _retireWhenSilent);
        set => Volatile.Write(ref _retireWhenSilent, value);
    }

    private bool _retireWhenSilent;

    /// <summary>True while the fade is above silence or heading up, i.e. the voice wants the device running.</summary>
    public bool IsAudible
    {
        get
        {
            var request = _fadeRequest;
            return request.Target > 0 || Volatile.Read(ref _fade) > 0;
        }
    }

    /// <summary>True once a play-once source has ended and every buffered sample has been played.</summary>
    public bool HasFinished => _sourceEnded && _ring.Count == 0;

    /// <summary>Position in the current loop, 0..1, for the progress ring.</summary>
    public double LoopProgress
    {
        get
        {
            long loop = _source.LoopFrames ?? EstimatedLoopFrames;
            if (loop <= 0)
            {
                return 0;
            }
            long played = Interlocked.Read(ref _framesPlayed);
            return (double)(played % loop) / loop;
        }
    }

    /// <summary>Linear gain the voice settles at (volume curve times loudness normalization).</summary>
    public void SetGain(float gain) => _targetGain = gain;

    /// <summary>Snaps the smoothed gain to its target, for a voice that hasn't started yet.</summary>
    public void SnapGain() => _gain = _targetGain;

    /// <summary>Ramps the fade layer to <paramref name="target"/> (0 or 1) over <paramref name="seconds"/>.</summary>
    public void FadeTo(float target, double seconds)
    {
        int frames = (int)Math.Round(Math.Max(0, seconds) * Mixer.SampleRate);
        _fadeRequest = new FadeRequest(target, frames, _fadeRequest.Id + 1);
    }

    /// <summary>Decode thread: tops the ring up. Returns true if it did any work.</summary>
    public bool Pump()
    {
        if (_sourceEnded || _ring.Free < PumpChunk)
        {
            return false;
        }
        int read = _source.Read(_pumpBuffer);
        _ring.Write(_pumpBuffer.AsSpan(0, read));
        if (read < _pumpBuffer.Length && _source.IsEnded)
        {
            _sourceEnded = true;
        }
        return read > 0;
    }

    /// <summary>Decode thread: needs topping up soon.</summary>
    public bool IsHungry => !_sourceEnded && _ring.Free >= PumpChunk;

    public void DisposeSource() => _source.Dispose();

    /// <summary>Audio thread: adds this voice into <paramref name="mix"/>.</summary>
    public void MixInto(Span<float> mix, Span<float> scratch)
    {
        ApplyFadeRequest();
        if (_fade <= 0 && _fadeTarget <= 0)
        {
            return;
        }

        // Before the first samples are decoded, hold the fade where it is, so a
        // fade-in starts with the sound rather than finishing in silence.
        int available = _ring.Read(scratch[..Math.Min(mix.Length, _ring.Count & ~1)]);
        if (available == 0)
        {
            return;
        }

        float target = _targetGain;
        float gain = _gain;
        float fade = _fade;
        for (int i = 0; i < available; i += 2)
        {
            gain += (target - gain) * GainSmoothing;
            if (fade != _fadeTarget)
            {
                fade += _fadeStep;
                if ((_fadeStep > 0 && fade >= _fadeTarget) || (_fadeStep < 0 && fade <= _fadeTarget) || _fadeStep == 0)
                {
                    fade = _fadeTarget;
                }
            }
            float level = gain * fade;
            mix[i] += scratch[i] * level;
            mix[i + 1] += scratch[i + 1] * level;
        }
        _gain = gain;
        Volatile.Write(ref _fade, fade);
        Interlocked.Add(ref _framesPlayed, available / 2);
    }

    private void ApplyFadeRequest()
    {
        var request = _fadeRequest;
        if (request.Id == _appliedFadeId)
        {
            return;
        }
        _appliedFadeId = request.Id;
        _fadeTarget = request.Target;
        _fadeStep = request.Frames == 0 ? 0 : (request.Target - _fade) / request.Frames;
        if (request.Frames == 0)
        {
            Volatile.Write(ref _fade, request.Target);
        }
    }

    private sealed record FadeRequest(float Target, int Frames, int Id);
}
