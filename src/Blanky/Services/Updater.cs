using System.Diagnostics;
using Blanky.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;

namespace Blanky.Services;

/// <summary>
/// Keeps an installed Blanky up to date from GitHub Releases. Checks at launch and
/// every few hours, downloads in the background, then offers a restart. An update
/// that's never restarted for is applied when Blanky closes. Does nothing in dev
/// builds, which aren't installed.
/// </summary>
internal sealed partial class Updater : ObservableObject, IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateManager _manager;
    private readonly string[] _restartArgs;
    private readonly CancellationTokenSource _stop = new();
    private VelopackAsset? _ready;

    /// <param name="source">A local folder or URL to update from instead of GitHub (testing only).</param>
    /// <param name="restartArgs">Arguments for the relaunched app, so test runs keep their settings.</param>
    public Updater(string? source = null, string[]? restartArgs = null)
    {
        _restartArgs = restartArgs ?? [];
        _manager = source is null
            ? new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: false))
            : new UpdateManager(source);
    }

    /// <summary>An update is downloaded and waiting for a restart.</summary>
    [ObservableProperty]
    public partial bool IsUpdateReady { get; private set; }

    /// <summary>The downloaded update's version, e.g. "1.2.0".</summary>
    [ObservableProperty]
    public partial string ReadyVersion { get; private set; } = "";

    /// <summary>Starts checking in the background. Call on the UI thread, which property changes are raised on.</summary>
    public void Start() => _ = RunAsync(_stop.Token);

    /// <summary>Hands the update to Velopack to apply once Blanky exits, then relaunch. The caller then closes the window.</summary>
    public void PrepareRestart() => ApplyOnExit(restart: true);

    /// <summary>Applies a downloaded update once Blanky exits, without relaunching. Called during shutdown.</summary>
    public void ApplyOnExit() => ApplyOnExit(restart: false);

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        if (!_manager.IsInstalled)
        {
            Trace.TraceInformation("Updates: not an installed copy, so not checking");
            return;
        }
        if (_manager.UpdatePendingRestart is { } pending)
        {
            MarkReady(pending);
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(CheckInterval);
            do
            {
                if (await TryDownloadAsync(token) is { } downloaded)
                {
                    MarkReady(downloaded);
                    return;
                }
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task<VelopackAsset?> TryDownloadAsync(CancellationToken token)
    {
        try
        {
            if (await _manager.CheckForUpdatesAsync() is not { } update)
            {
                return null;
            }
            Trace.TraceInformation($"Updates: downloading {update.TargetFullRelease.Version}");
            await _manager.DownloadUpdatesAsync(update, null, token);
            return update.TargetFullRelease;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Offline, rate-limited or GitHub is down: try again next time.
            Trace.TraceWarning($"Updates: check failed: {ex.Message}");
            return null;
        }
    }

    private void MarkReady(VelopackAsset asset)
    {
        Trace.TraceInformation($"Updates: {asset.Version} is ready");
        _ready = asset;
        ReadyVersion = asset.Version.ToString();
        IsUpdateReady = true;
    }

    private void ApplyOnExit(bool restart)
    {
        if (_ready is not { } asset)
        {
            return;
        }
        _ready = null;
        try
        {
            _manager.WaitExitThenApplyUpdates(asset, silent: true, restart: restart, _restartArgs);
            Trace.TraceInformation($"Updates: {asset.Version} will be applied on exit (restart={restart})");
        }
        catch (Exception ex)
        {
            // The update stays downloaded; Velopack applies it on the next launch instead.
            Trace.TraceWarning($"Updates: couldn't hand off {asset.Version}: {ex.Message}");
        }
    }
}
