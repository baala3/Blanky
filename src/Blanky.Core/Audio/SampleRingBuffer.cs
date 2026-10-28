namespace Blanky.Core.Audio;

/// <summary>
/// Lock-free single-producer, single-consumer ring of float samples. The decode
/// thread writes and the audio thread reads, so neither ever blocks the other.
/// </summary>
internal sealed class SampleRingBuffer
{
    private readonly float[] _buffer;
    private long _written;
    private long _read;

    public SampleRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _buffer = new float[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count => (int)(Volatile.Read(ref _written) - Volatile.Read(ref _read));

    public int Free => Capacity - Count;

    /// <summary>Producer side. Writes as much of <paramref name="source"/> as fits and returns the count written.</summary>
    public int Write(ReadOnlySpan<float> source)
    {
        long written = _written;
        int count = Math.Min(source.Length, Capacity - (int)(written - Volatile.Read(ref _read)));
        int start = (int)(written % Capacity);
        int first = Math.Min(count, Capacity - start);
        source[..first].CopyTo(_buffer.AsSpan(start));
        source[first..count].CopyTo(_buffer);
        Volatile.Write(ref _written, written + count);
        return count;
    }

    /// <summary>Consumer side. Reads up to <paramref name="destination"/>.Length samples and returns the count read.</summary>
    public int Read(Span<float> destination)
    {
        long read = _read;
        int count = Math.Min(destination.Length, (int)(Volatile.Read(ref _written) - read));
        int start = (int)(read % Capacity);
        int first = Math.Min(count, Capacity - start);
        _buffer.AsSpan(start, first).CopyTo(destination);
        _buffer.AsSpan(0, count - first).CopyTo(destination[first..]);
        Volatile.Write(ref _read, read + count);
        return count;
    }

    /// <summary>Consumer side. Drops everything buffered (used when a voice restarts from the top).</summary>
    public void Clear() => Volatile.Write(ref _read, Volatile.Read(ref _written));
}
