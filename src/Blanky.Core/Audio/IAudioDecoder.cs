namespace Blanky.Core.Audio;

/// <summary>
/// A forward-only decoder producing interleaved stereo float frames at the mixer's
/// sample rate. Implementations handle container decoding, channel mapping and
/// resampling, so everything downstream deals in one format.
/// </summary>
public interface IAudioDecoder : IDisposable
{
    /// <summary>Fills <paramref name="buffer"/> and returns the number of floats written. 0 means end of stream.</summary>
    int Read(Span<float> buffer);
}

public interface IAudioDecoderFactory
{
    /// <summary>Opens <paramref name="path"/> for decoding to stereo at <paramref name="sampleRate"/>.</summary>
    IAudioDecoder Open(string path, int sampleRate);
}

/// <summary>
/// The playback device. The engine renders at <see cref="Mixer.SampleRate"/> and the
/// output converts to the device format. Implementations follow the system default
/// device on their own.
/// </summary>
public interface IAudioOutput : IAsyncDisposable
{
    /// <summary>Starts (or resumes) pulling from <paramref name="source"/>.</summary>
    Task StartAsync(NAudio.Wave.ISampleProvider source);

    /// <summary>Stops pulling and releases the device stream, so Windows can sleep.</summary>
    Task StopAsync();

    /// <summary>Raised (on any thread) when playback stops on its own, e.g. the device failed.</summary>
    event EventHandler<Exception?>? Faulted;
}
