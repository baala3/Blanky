using System.Diagnostics;

namespace Blanky.Core.Audio;

/// <summary>Per-voice playback options.</summary>
/// <param name="Loop">False plays the file once and raises <see cref="AudioEngine.VoiceFinished"/>.</param>
/// <param name="EstimatedDuration">Used for loop progress until the first pass has been measured.</param>
public readonly record struct VoiceOptions(bool Loop = true, TimeSpan EstimatedDuration = default);

/// <summary>
/// Plays any number of looping sounds through one output. Callers use string keys
/// (a sound's file name) and never see voices or threads.
/// </summary>
/// <remarks>
/// A single background thread decodes ahead for every voice and owns the output
/// device: it starts the device when anything becomes audible and stops it after
/// <see cref="IdleShutdown"/> of silence, so a paused app never holds the audio
/// stream open (an open stream keeps Windows from sleeping). Public members are
/// meant to be called from one thread (the UI thread). Events are posted to the
/// <see cref="SynchronizationContext"/> captured at construction.
/// </remarks>
public sealed class AudioEngine : Playback.ISoundPlayer, IAsyncDisposable
{
    public static readonly TimeSpan IdleShutdown = TimeSpan.FromSeconds(2);

    private readonly IAudioDecoderFactory _decoders;
    private readonly IAudioOutput _output;
    private readonly int _seamFrames;
    private readonly SynchronizationContext? _events;
    private readonly Mixer _mixer = new();
    private readonly Dictionary<string, Voice> _voices = [];
    private readonly Lock _voicesLock = new();
    private readonly Thread _worker;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _disposed;
    private bool _outputRunning;

    /// <param name="decoders">Opens audio files.</param>
    /// <param name="output">The device. The engine starts and stops it.</param>
    /// <param name="seamCrossfade">Crossfade at each loop seam (zero for gapless decoders).</param>
    public AudioEngine(IAudioDecoderFactory decoders, IAudioOutput output, TimeSpan seamCrossfade = default)
    {
        _decoders = decoders;
        _output = output;
        _seamFrames = (int)(seamCrossfade.TotalSeconds * Mixer.SampleRate);
        _events = SynchronizationContext.Current;
        _output.Faulted += OnOutputFaulted;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Blanky decoder", Priority = ThreadPriority.AboveNormal };
        _worker.Start();
    }

    /// <summary>A play-once voice reached its end and was removed.</summary>
    public event EventHandler<string>? VoiceFinished;

    /// <summary>A voice failed to decode and was removed.</summary>
    public event EventHandler<VoiceFailedEventArgs>? VoiceFailed;

    /// <summary>Overall output level, 0..1 (linear, like Blankie's main mixer volume).</summary>
    public float MasterVolume
    {
        get => _mixer.MasterVolume;
        set => _mixer.MasterVolume = value;
    }

    /// <summary>True when the device stream is open (for tests and diagnostics).</summary>
    public bool IsOutputRunning => Volatile.Read(ref _outputRunning);

    /// <summary>Starts or resumes <paramref name="key"/>, fading in over <paramref name="fade"/>.</summary>
    public void Play(string key, string path, float gain, TimeSpan fade, VoiceOptions options = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Voice voice;
        lock (_voicesLock)
        {
            if (!_voices.TryGetValue(key, out voice!) || voice.RetireWhenSilent)
            {
                if (voice is not null)
                {
                    // A stop still fading out: let it finish under its own key and start fresh.
                    _voices.Remove(key);
                }
                var estimated = (long)(options.EstimatedDuration.TotalSeconds * Mixer.SampleRate);
                var source = new LoopingSource(() => _decoders.Open(path, Mixer.SampleRate), options.Loop, _seamFrames);
                voice = new Voice(key, source, estimated);
                voice.SetGain(gain);
                voice.SnapGain();
                _voices[key] = voice;
                _mixer.Add(voice);
            }
        }
        voice.SetGain(gain);
        voice.FadeTo(1, fade.TotalSeconds);
        _wake.Set();
    }

    /// <summary>Fades <paramref name="key"/> to silence but keeps its position for a later <see cref="Play"/>.</summary>
    public void Pause(string key, TimeSpan fade)
    {
        if (TryGet(key) is { } voice)
        {
            voice.FadeTo(0, fade.TotalSeconds);
        }
    }

    /// <summary>Fades <paramref name="key"/> out and releases it. The next play starts from the top.</summary>
    public void Stop(string key, TimeSpan fade)
    {
        if (TryGet(key) is { } voice)
        {
            voice.RetireWhenSilent = true;
            voice.FadeTo(0, fade.TotalSeconds);
            _wake.Set();
        }
    }

    public void SetGain(string key, float gain) => TryGet(key)?.SetGain(gain);

    /// <summary>Position in the current loop (0..1), or null if <paramref name="key"/> isn't loaded.</summary>
    public double? GetLoopProgress(string key) => TryGet(key)?.LoopProgress;

    public bool IsLoaded(string key) => TryGet(key) is not null;

    private Voice? TryGet(string key)
    {
        lock (_voicesLock)
        {
            return _voices.TryGetValue(key, out var voice) && !voice.RetireWhenSilent ? voice : null;
        }
    }

    private void WorkerLoop()
    {
        var retiring = new List<Voice>();
        var silentSince = Stopwatch.StartNew();
        while (!_disposed)
        {
            bool busy = false;
            bool anyAudible = false;
            foreach (var voice in _mixer.Voices)
            {
                try
                {
                    busy |= voice.Pump();
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
                {
                    Trace.TraceError($"Sound '{voice.Key}' failed: {ex}");
                    Retire(voice);
                    Post(() => VoiceFailed?.Invoke(this, new VoiceFailedEventArgs(voice.Key, ex)));
                    continue;
                }

                if (voice.HasFinished)
                {
                    Retire(voice);
                    Post(() => VoiceFinished?.Invoke(this, voice.Key));
                }
                else if (voice.RetireWhenSilent && !voice.IsAudible)
                {
                    Retire(voice);
                }
                else
                {
                    anyAudible |= voice.IsAudible;
                }
            }

            UpdateOutput(anyAudible, silentSince);

            // Sleep briefly when every ring is full; a play or stop wakes us early.
            if (!busy)
            {
                _wake.WaitOne(10);
            }
        }
    }

    private void Retire(Voice voice)
    {
        _mixer.Remove(voice);
        lock (_voicesLock)
        {
            if (_voices.TryGetValue(voice.Key, out var current) && current == voice)
            {
                _voices.Remove(voice.Key);
            }
        }
        voice.DisposeSource();
    }

    private void UpdateOutput(bool anyAudible, Stopwatch silentSince)
    {
        if (anyAudible)
        {
            silentSince.Restart();
            if (!_outputRunning)
            {
                RunOutputCommand(() => _output.StartAsync(_mixer), running: true);
            }
        }
        else if (_outputRunning && silentSince.Elapsed >= IdleShutdown)
        {
            RunOutputCommand(_output.StopAsync, running: false);
        }
    }

    private void RunOutputCommand(Func<Task> command, bool running)
    {
        try
        {
            command().GetAwaiter().GetResult();
            Volatile.Write(ref _outputRunning, running);
            Trace.TraceInformation(running ? "Audio output started" : "Audio output stopped (idle)");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        {
            // No device right now (e.g. none plugged in). Retry on the next pass.
            Trace.TraceWarning($"Audio output {(running ? "start" : "stop")} failed: {ex.Message}");
            Volatile.Write(ref _outputRunning, false);
            _wake.WaitOne(1000);
        }
    }

    private void OnOutputFaulted(object? sender, Exception? error)
    {
        Trace.TraceWarning($"Audio output stopped unexpectedly: {error?.Message}");
        // The worker restarts the device on its next pass if anything is audible.
        Volatile.Write(ref _outputRunning, false);
        _wake.Set();
    }

    // Never raise events inline on the worker: a handler that disposes the engine
    // would then join the worker from itself.
    private void Post(Action action)
    {
        if (_events is null)
        {
            ThreadPool.QueueUserWorkItem(_ => action());
        }
        else
        {
            _events.Post(_ => action(), null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _wake.Set();
        if (Thread.CurrentThread != _worker)
        {
            _worker.Join();
        }
        _output.Faulted -= OnOutputFaulted;
        await _output.DisposeAsync().ConfigureAwait(false);
        foreach (var voice in _mixer.Voices)
        {
            voice.DisposeSource();
        }
        _wake.Dispose();
    }
}

public sealed class VoiceFailedEventArgs(string key, Exception error) : EventArgs
{
    public string Key { get; } = key;

    public Exception Error { get; } = error;
}
