using Blanky.Core.Model;
using Blanky.Core.Storage;

namespace Blanky.Core.Playback;

/// <summary>
/// The live <see cref="AppSettings"/>. Changes go through <see cref="Update"/>,
/// raise <see cref="Changed"/> immediately and are written to disk shortly after,
/// so dragging the volume slider doesn't write a file per pixel.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly DataStore _store;
    private readonly ITimer _saveTimer;
    private readonly Lock _saveLock = new();
    private bool _dirty;

    public SettingsService(DataStore store, TimeProvider? time = null)
    {
        _store = store;
        Current = store.LoadSettings();
        _saveTimer = (time ?? TimeProvider.System).CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public AppSettings Current { get; private set; }

    /// <summary>Raised on the calling thread with (old, new) after every change.</summary>
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        var old = Current;
        var updated = change(old);
        if (updated == old)
        {
            return;
        }
        lock (_saveLock)
        {
            Current = updated;
            _dirty = true;
        }
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        Changed?.Invoke(this, new SettingsChangedEventArgs(old, updated));
    }

    public bool IsStarred(PlayableItem item) => Current.StarredItems.Contains(item.Token);

    public void SetStarred(PlayableItem item, bool starred) => Update(s => s with
    {
        StarredItems = starred
            ? s.StarredItems.Contains(item.Token) ? s.StarredItems : [.. s.StarredItems, item.Token]
            : [.. s.StarredItems.Where(t => t != item.Token)],
    });

    /// <summary>Writes pending changes now (also called on exit).</summary>
    public void Flush()
    {
        AppSettings snapshot;
        lock (_saveLock)
        {
            if (!_dirty)
            {
                return;
            }
            _dirty = false;
            snapshot = Current;
        }
        _store.SaveSettings(snapshot);
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}

public sealed class SettingsChangedEventArgs(AppSettings old, AppSettings current) : EventArgs
{
    public AppSettings Old { get; } = old;

    public AppSettings Current { get; } = current;
}
