using Blanky.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Blanky.Platform;

/// <summary>Decodes any format Media Foundation supports into 48 kHz stereo float.</summary>
internal sealed class MediaFoundationDecoderFactory : IAudioDecoderFactory
{
    public IAudioDecoder Open(string path, int sampleRate) => new MediaFoundationDecoder(path, sampleRate);

    private sealed class MediaFoundationDecoder : IAudioDecoder
    {
        private readonly MediaFoundationReader _reader;
        private readonly ISampleProvider _samples;

        public MediaFoundationDecoder(string path, int sampleRate)
        {
            _reader = new MediaFoundationReader(path, new MediaFoundationReader.MediaFoundationReaderSettings
            {
                RequestFloatOutput = true,
                RepositionInRead = false,
            });
            try
            {
                ISampleProvider samples = _reader.ToSampleProvider();
                samples = samples.WaveFormat.Channels switch
                {
                    1 => new MonoToStereoSampleProvider(samples),
                    2 => samples,
                    _ => new FirstTwoChannels(samples),
                };
                if (samples.WaveFormat.SampleRate != sampleRate)
                {
                    samples = new WdlResamplingSampleProvider(samples, sampleRate);
                }
                _samples = samples;
            }
            catch
            {
                _reader.Dispose();
                throw;
            }
        }

        public int Read(Span<float> buffer) => _samples.Read(buffer);

        public void Dispose() => _reader.Dispose();
    }

    /// <summary>Keeps front left and right of a multichannel file.</summary>
    private sealed class FirstTwoChannels(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);

        public int Read(Span<float> buffer)
        {
            int frames = buffer.Length / 2;
            if (_buffer.Length < frames * _channels)
            {
                _buffer = new float[frames * _channels];
            }
            int read = source.Read(_buffer.AsSpan(0, frames * _channels)) / _channels;
            for (int i = 0; i < read; i++)
            {
                buffer[2 * i] = _buffer[i * _channels];
                buffer[(2 * i) + 1] = _buffer[(i * _channels) + 1];
            }
            return read * 2;
        }
    }
}

/// <summary>
/// Shared-mode WASAPI output that follows the Windows default device (headphones
/// plugged in or out, Bluetooth connecting) without any app code.
/// </summary>
internal sealed class WasapiAudioOutput : IAudioOutput
{
    private WasapiPlayer? _player;

    public event EventHandler<Exception?>? Faulted;

    public async Task StartAsync(ISampleProvider source)
    {
        if (_player is null)
        {
            var player = await new WasapiPlayerBuilder()
                .WithDefaultDeviceStreamRouting()
                .WithCategory(NAudio.CoreAudioApi.AudioStreamCategory.Media)
                .WithLatency(100)
                .BuildAsync()
                .ConfigureAwait(false);
            player.Init(new SampleToWaveProvider(source));
            player.PlaybackStopped += OnPlaybackStopped;
            _player = player;
        }
        _player.Play();
    }

    public async Task StopAsync()
    {
        if (_player is { } player)
        {
            // Dispose rather than pause: an open stream keeps Windows awake.
            _player = null;
            player.PlaybackStopped -= OnPlaybackStopped;
            await player.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null && sender == _player)
        {
            _player = null;
            Faulted?.Invoke(this, e.Exception);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
