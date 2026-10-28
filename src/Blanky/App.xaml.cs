using Blanky.Core;
using Blanky.Core.Audio;
using Blanky.Core.Playback;
using Blanky.Core.Storage;
using Blanky.Platform;
using Blanky.Services;
using Blanky.Views;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Blanky;

/// <summary>Composition root: builds the services once and owns their lifetime.</summary>
public partial class App : Application
{
    private readonly string[] _args;
    private AudioEngine? _engine;
    private MainWindow? _window;

    public App(string[] args)
    {
        _args = args;
        InitializeComponent();
        UnhandledException += (_, e) => System.Diagnostics.Trace.TraceError($"Unhandled (XAML): {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => System.Diagnostics.Trace.TraceError($"Unhandled (AppDomain): {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => System.Diagnostics.Trace.TraceError($"Unobserved task: {e.Exception}");
    }

    public static new App Current => (App)Application.Current;

    internal AudioManager Audio { get; private set; } = null!;

    internal SettingsService Settings { get; private set; } = null!;

    internal ThemeManager Theme { get; private set; } = null!;

    internal AppPaths Paths { get; private set; } = null!;

    /// <summary>Debug-only: <c>--capture path.png</c> screenshots the window and exits.</summary>
    internal string? CapturePath { get; private set; }

    /// <summary>Debug-only: <c>--exit-after seconds</c> closes the window after a delay (lifecycle testing).</summary>
    internal TimeSpan? ExitAfter { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CapturePath = ArgValue("--capture");
        ExitAfter = double.TryParse(ArgValue("--exit-after"), System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds) : null;
        Paths = ArgValue("--data") is { } dataRoot ? new AppPaths(dataRoot) : AppPaths.Default();
        StartLog();
        var store = new DataStore(Paths);
        Settings = new SettingsService(store);
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var decoders = new MediaFoundationDecoderFactory();
        _engine = new AudioEngine(decoders, new WasapiAudioOutput());
        var library = new SoundLibrary(store, Settings, decoders, Path.Combine(assets, "sounds.json"), Path.Combine(assets, "Sounds"));
        Audio = new AudioManager(_engine, Settings, new PresetManager(store), library);
        Theme = new ThemeManager(Resources);

        Audio.Restore();
        if (CapturePath is not null || ExitAfter is not null)
        {
            // Screenshots are silent and reproducible.
            _engine.MasterVolume = 0;
            if (ArgValue("--demo") is { } demo)
            {
                foreach (var name in demo.Split(','))
                {
                    if (library.Find(name) is { } sound)
                    {
                        Audio.ToggleOrResume(sound);
                    }
                }
            }
        }

        _window = new MainWindow();
        AppInstance.GetCurrent().Activated += (_, _) => _window.DispatcherQueue.TryEnqueue(_window.BringToFront);
        _window.Activate();
    }

    /// <summary>Stops audio and saves everything. Called once, when the window closes.</summary>
    internal void Shutdown()
    {
        System.Diagnostics.Trace.TraceInformation("Shutdown: begin");
        Audio.Dispose();
        Settings.Dispose();
        System.Diagnostics.Trace.TraceInformation("Shutdown: state saved");

        // The engine's teardown never needs the UI thread, so blocking on it here is safe.
        if (_engine is { } engine)
        {
            bool stopped = Task.Run(() => engine.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));
            System.Diagnostics.Trace.TraceInformation($"Shutdown: audio stopped={stopped}");
        }
    }

    // A small rolling log (blanky.log next to the settings) for diagnosing audio and
    // device problems on users' machines. Restarted each launch, so it stays small.
    private void StartLog()
    {
        try
        {
            var writer = new StreamWriter(Paths.LogFile, append: false) { AutoFlush = true };
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(writer) { TraceOutputOptions = System.Diagnostics.TraceOptions.DateTime });
            System.Diagnostics.Trace.AutoFlush = true;
            System.Diagnostics.Trace.TraceInformation($"{AppInfo.Name} {AppInfo.Version} starting, data in {Paths.Root}");
        }
        catch (IOException)
        {
            // Another process holds the log; run without one.
        }
    }

    private string? ArgValue(string name)
    {
        int index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
    }
}
