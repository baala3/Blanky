using CommunityToolkit.Mvvm.ComponentModel;

namespace Blanky.Core.Playback;

/// <summary>
/// Pauses playback after a chosen duration. <see cref="Remaining"/> ticks once a
/// second for the UI. Events and property changes arrive on the
/// <see cref="SynchronizationContext"/> captured at construction.
/// </summary>
public sealed partial class SleepTimer : ObservableObject, IDisposable
{
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _context;
    private ITimer? _ticker;
    private DateTimeOffset _endsAt;

    public SleepTimer(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _context = SynchronizationContext.Current;
    }

    /// <summary>Raised when the countdown reaches zero.</summary>
    public event EventHandler? Expired;

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    [ObservableProperty]
    public partial TimeSpan Remaining { get; private set; }

    /// <summary>The duration the timer was last started with (plus any added time).</summary>
    [ObservableProperty]
    public partial TimeSpan Duration { get; private set; }

    /// <summary>Wall-clock time the timer fires, in local time.</summary>
    public DateTimeOffset? EndsAt => IsActive ? _endsAt.ToLocalTime() : null;

    public void Start(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        _endsAt = _time.GetUtcNow() + duration;
        Duration = duration;
        IsActive = true;
        _ticker?.Dispose();
        _ticker = _time.CreateTimer(_ => Post(Tick), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Tick();
        OnPropertyChanged(nameof(EndsAt));
    }

    public void AddTime(TimeSpan extra)
    {
        if (!IsActive)
        {
            return;
        }
        _endsAt += extra;
        Duration += extra;
        Tick();
        OnPropertyChanged(nameof(EndsAt));
    }

    public void Cancel()
    {
        _ticker?.Dispose();
        _ticker = null;
        IsActive = false;
        Remaining = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        OnPropertyChanged(nameof(EndsAt));
    }

    /// <summary>"1:05:09" or "4:59" style countdown.</summary>
    public static string Format(TimeSpan remaining)
    {
        var rounded = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, remaining.TotalSeconds)));
        return rounded.TotalHours >= 1
            ? $"{(int)rounded.TotalHours}:{rounded.Minutes:00}:{rounded.Seconds:00}"
            : $"{rounded.Minutes}:{rounded.Seconds:00}";
    }

    private void Tick()
    {
        if (!IsActive)
        {
            return;
        }
        var left = _endsAt - _time.GetUtcNow();
        if (left <= TimeSpan.Zero)
        {
            Cancel();
            Expired?.Invoke(this, EventArgs.Empty);
            return;
        }
        Remaining = left;
    }

    private void Post(Action action)
    {
        if (_context is null)
        {
            action();
        }
        else
        {
            // Callbacks queued before Dispose must not touch a UI that's shutting down.
            _context.Post(_ =>
            {
                if (!_disposed)
                {
                    action();
                }
            }, null);
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        _ticker?.Dispose();
    }
}
