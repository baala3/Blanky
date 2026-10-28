namespace Blanky.Core.Audio;

/// <summary>
/// Endless stereo stream over one audio file. At end of stream it reopens the file
/// and keeps going, optionally crossfading the last and first
/// <c>seamFrames</c> frames so a decoder that pads or trims the edges can't click.
/// Runs on the decode thread only.
/// </summary>
internal sealed class LoopingSource : IDisposable
{
    private readonly Func<IAudioDecoder> _open;
    private readonly bool _loop;
    private readonly float[] _tail;
    private readonly float[] _head;
    private IAudioDecoder? _decoder;
    private int _seamSamples;
    private int _tailCount;
    private long _emittedThisPass;
    private long _decodedThisPass;
    private bool _sourceDone;

    /// <param name="open">Opens a fresh decoder positioned at the start of the file.</param>
    /// <param name="loop">False plays the file once, then reports end of stream.</param>
    /// <param name="seamFrames">Frames crossfaded at each loop seam (0 = butt splice).</param>
    public LoopingSource(Func<IAudioDecoder> open, bool loop = true, int seamFrames = 0)
    {
        _open = open;
        _loop = loop;
        _seamSamples = Math.Max(0, seamFrames) * 2;
        _tail = new float[_seamSamples];
        _head = new float[_seamSamples];
    }

    /// <summary>Frames in one pass through the file, once the first pass has finished.</summary>
    public long? LoopFrames { get; private set; }

    /// <summary>True once a non-looping source has delivered its last sample.</summary>
    public bool IsEnded => _sourceDone && _tailCount == 0;

    /// <summary>Fills <paramref name="destination"/> as far as possible. Returns fewer samples only at the end of a non-looping source.</summary>
    /// <exception cref="InvalidDataException">The file decodes to no audio at all.</exception>
    public int Read(Span<float> destination)
    {
        int total = 0;
        while (total < destination.Length && !IsEnded)
        {
            // Samples held back for a seam that's no longer used (or the end of a
            // play-once source) go out before anything new.
            if ((_seamSamples == 0 || _sourceDone) && _tailCount > 0)
            {
                int flushed = FlushTail(destination[total..]);
                total += flushed;
                _emittedThisPass += flushed;
                continue;
            }
            if (_sourceDone)
            {
                break;
            }

            _decoder ??= _open();
            int read;
            int decoded;
            if (_seamSamples == 0)
            {
                read = decoded = _decoder.Read(destination[total..]);
            }
            else
            {
                read = ReadThroughDelay(destination[total..], out decoded);
            }
            total += read;
            _emittedThisPass += read;
            _decodedThisPass += decoded;
            if (decoded > 0)
            {
                continue;
            }

            EndPass();
            if (!_loop)
            {
                _sourceDone = true;
                continue;
            }
            _decoder = _open();
            if (_seamSamples > 0)
            {
                CrossfadeSeam();
            }
        }
        return total;
    }

    // Holds the most recent _seamSamples back, so the end of a pass is still on
    // hand to crossfade when the decoder reports end of stream. Returns the count
    // emitted; <paramref name="decoded"/> is the count read from the decoder (0 at end).
    private int ReadThroughDelay(Span<float> destination, out int decoded)
    {
        Span<float> scratch = stackalloc float[Math.Min(destination.Length, 4096)];
        int read = _decoder!.Read(scratch);
        decoded = read;
        if (read == 0)
        {
            return 0;
        }

        int combined = _tailCount + read;
        int emit = Math.Max(0, combined - _seamSamples);
        int fromTail = Math.Min(emit, _tailCount);
        _tail.AsSpan(0, fromTail).CopyTo(destination);
        scratch[..(emit - fromTail)].CopyTo(destination[fromTail..]);

        // Rebuild the tail from the unemitted remainder of tail + scratch.
        int keepFromTail = _tailCount - fromTail;
        _tail.AsSpan(fromTail, keepFromTail).CopyTo(_tail);
        scratch[(emit - fromTail)..read].CopyTo(_tail.AsSpan(keepFromTail));
        _tailCount = keepFromTail + (read - (emit - fromTail));
        return emit;
    }

    private void EndPass()
    {
        _decoder?.Dispose();
        _decoder = null;
        if (_decodedThisPass == 0)
        {
            throw new InvalidDataException("The file contains no audio.");
        }

        // A file shorter than two seams can't be crossfaded sensibly: splice it instead.
        if (_seamSamples > 0 && _decodedThisPass < 2L * _seamSamples)
        {
            _seamSamples = 0;
        }

        if (LoopFrames is null)
        {
            LoopFrames = (_emittedThisPass + (_seamSamples == 0 ? _tailCount : 0)) / 2;
        }
        _emittedThisPass = 0;
        _decodedThisPass = 0;
    }

    // Equal-power crossfade of the held-back tail into the head of the next pass.
    // The tail becomes: unblended tail prefix, blended overlap, unblended head
    // remainder. Its length is max(tail, head), so it always fits the seam buffer,
    // and later reads push it out ahead of fresh decoder output.
    private void CrossfadeSeam()
    {
        int headCount = 0;
        while (headCount < _seamSamples)
        {
            int read = _decoder!.Read(_head.AsSpan(headCount, _seamSamples - headCount));
            if (read == 0)
            {
                break;
            }
            headCount += read;
        }
        _decodedThisPass = headCount;

        int overlap = Math.Min(_tailCount, headCount);
        int prefix = _tailCount - overlap;
        int frames = overlap / 2;
        for (int i = 0; i < overlap; i++)
        {
            double t = frames <= 1 ? 1 : (double)(i / 2) / (frames - 1);
            _tail[prefix + i] = (float)((_tail[prefix + i] * Math.Cos(t * Math.PI / 2))
                + (_head[i] * Math.Sin(t * Math.PI / 2)));
        }
        _head.AsSpan(overlap, headCount - overlap).CopyTo(_tail.AsSpan(prefix + overlap));
        _tailCount = prefix + headCount;
    }

    private int FlushTail(Span<float> destination)
    {
        int count = Math.Min(_tailCount, destination.Length);
        _tail.AsSpan(0, count).CopyTo(destination);
        _tail.AsSpan(count, _tailCount - count).CopyTo(_tail);
        _tailCount -= count;
        return count;
    }

    public void Dispose()
    {
        _decoder?.Dispose();
        _decoder = null;
    }
}
