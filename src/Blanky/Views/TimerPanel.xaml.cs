using System.ComponentModel;
using Blanky.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Blanky.Views;

/// <summary>Sleep timer: pick hours and minutes, then add time or cancel while it runs.</summary>
public sealed partial class TimerPanel : UserControl
{
    public TimerPanel()
    {
        InitializeComponent();
        Timer.PropertyChanged += OnTimerChanged;
        Unloaded += (_, _) => Timer.PropertyChanged -= OnTimerChanged;
        Refresh();
    }

    public SleepTimer Timer { get; } = App.Current.Audio.SleepTimer;

    /// <summary>Raised after starting or cancelling, so a hosting flyout can close.</summary>
    public event EventHandler? Done;

    public void Refresh()
    {
        bool running = Timer.IsActive;
        Setup.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        StartButton.Visibility = Setup.Visibility;
        Running.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        RunningButtons.Visibility = Running.Visibility;
        EndsAtText.Text = Timer.EndsAt is { } end ? $"Pausing at {end:t}" : "";
    }

    private void OnTimerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SleepTimer.IsActive) or nameof(SleepTimer.EndsAt) or nameof(SleepTimer.Duration))
        {
            Refresh();
        }
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        var duration = TimeSpan.FromHours(Value(Hours)) + TimeSpan.FromMinutes(Value(Minutes));
        if (duration <= TimeSpan.Zero)
        {
            Minutes.Value = 30;
            return;
        }
        Timer.Start(duration);
        Done?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddTime(object sender, RoutedEventArgs e) => Timer.AddTime(TimeSpan.FromMinutes(15));

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Timer.Cancel();
        Done?.Invoke(this, EventArgs.Empty);
    }

    private static double Value(NumberBox box) => double.IsNaN(box.Value) ? 0 : box.Value;
}
