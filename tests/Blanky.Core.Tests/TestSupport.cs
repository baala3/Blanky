using Blanky.Core.Audio;
using Blanky.Core.Playback;
using Blanky.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Blanky.Core.Tests;

/// <summary>Decodes every path as a stereo sine of a fixed length, counting how often files are opened.</summary>
internal sealed class SineDecoderFactory(double seconds = 1, double amplitude = 0.1, double frequency = 1000) : IAudioDecoderFactory
{
    public int Opens { get; private set; }

    public IAudioDecoder Open(string path, int sampleRate)
    {
        Opens++;
        return new SineDecoder((long)(seconds * sampleRate), sampleRate, amplitude, frequency);
    }
}

internal sealed class SineDecoder(long frames, int sampleRate, double amplitude, double frequency) : IAudioDecoder
{
    private long _frame;

    public int Read(Span<float> buffer)
    {
        int count = (int)Math.Min(buffer.Length / 2, frames - _frame);
        for (int i = 0; i < count; i++, _frame++)
        {
            var sample = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * _frame / sampleRate));
            buffer[2 * i] = sample;
            buffer[(2 * i) + 1] = sample;
        }
        return count * 2;
    }

    public void Dispose()
    {
    }
}

/// <summary>Decodes a fixed sample sequence (left = right = the frame index), to check splices exactly.</summary>
internal sealed class RampDecoder(int frames, int chunk = 7) : IAudioDecoder
{
    private int _frame;

    public int Read(Span<float> buffer)
    {
        int count = Math.Min(Math.Min(buffer.Length / 2, chunk), frames - _frame);
        for (int i = 0; i < count; i++, _frame++)
        {
            buffer[2 * i] = _frame;
            buffer[(2 * i) + 1] = _frame;
        }
        return count * 2;
    }

    public void Dispose()
    {
    }
}

/// <summary>Records what the playback rules asked the engine to do.</summary>
internal sealed class FakePlayer : ISoundPlayer
{
    public Dictionary<string, string> State { get; } = [];

    public Dictionary<string, float> Gains { get; } = [];

    public float MasterVolume { get; set; } = 1;

    public event EventHandler<string>? VoiceFinished;

    public event EventHandler<VoiceFailedEventArgs>? VoiceFailed;

    public void Play(string key, string path, float gain, TimeSpan fade, VoiceOptions options = default)
    {
        State[key] = "playing";
        Gains[key] = gain;
    }

    public void Pause(string key, TimeSpan fade)
    {
        if (State.ContainsKey(key))
        {
            State[key] = "paused";
        }
    }

    public void Stop(string key, TimeSpan fade) => State.Remove(key);

    public void SetGain(string key, float gain)
    {
        if (State.ContainsKey(key))
        {
            Gains[key] = gain;
        }
    }

    public double? GetLoopProgress(string key) => State.ContainsKey(key) ? 0.5 : null;

    public IEnumerable<string> Playing => State.Where(kv => kv.Value == "playing").Select(kv => kv.Key).Order();

    public void Finish(string key)
    {
        State.Remove(key);
        VoiceFinished?.Invoke(this, key);
    }

    public void Fail(string key)
    {
        State.Remove(key);
        VoiceFailed?.Invoke(this, new VoiceFailedEventArgs(key, new InvalidDataException("bad file")));
    }
}

/// <summary>A full app core over a temp data folder, with fake audio and time.</summary>
internal sealed class TestApp : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("blanky-tests-");

    public TestApp(bool reuseRoot = false, DirectoryInfo? root = null)
    {
        if (reuseRoot && root is not null)
        {
            _root.Delete(true);
            _root = root;
        }

        // Run timer callbacks inline instead of posting them to xUnit's context.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            Paths = new AppPaths(_root.FullName);
            Store = new DataStore(Paths);
            Settings = new SettingsService(Store, Time);
            Library = new SoundLibrary(Store, Settings, Decoders, CatalogPath, Path.Combine(_root.FullName, "builtin"));
            Presets = new PresetManager(Store);
            Audio = new AudioManager(Player, Settings, Presets, Library, Time);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    public static string CatalogPath => Path.Combine(AppContext.BaseDirectory, "Assets", "sounds.json");

    public DirectoryInfo Root => _root;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 28, 22, 0, 0, TimeSpan.Zero));

    public FakePlayer Player { get; } = new();

    public SineDecoderFactory Decoders { get; } = new();

    public AppPaths Paths { get; }

    public DataStore Store { get; }

    public SettingsService Settings { get; }

    public SoundLibrary Library { get; }

    public PresetManager Presets { get; }

    public AudioManager Audio { get; }

    public SoundItem Sound(string fileName) => Library.Find(fileName) ?? throw new KeyNotFoundException(fileName);

    /// <summary>Writes a small file the fake decoder will "decode" as a sine.</summary>
    public string MakeAudioFile(string name, string content = "audio")
    {
        var path = Path.Combine(_root.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        Audio.Dispose();
        Settings.Dispose();
    }

    public void DeleteRoot() => _root.Delete(recursive: true);
}
