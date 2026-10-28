using Blanky.Core.Playback;
using Microsoft.UI.Xaml;

namespace Blanky.Views;

/// <summary>Small conversions for x:Bind function bindings.</summary>
internal static class Ui
{
    public static Visibility Not(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility IsTransientMode(PlaybackMode mode) => mode == PlaybackMode.Preset ? Visibility.Collapsed : Visibility.Visible;

    public static string Countdown(TimeSpan remaining) => SleepTimer.Format(remaining);

    /// <summary>"Save as Preset" shows on the all-sounds page once something is selected.</summary>
    public static Visibility CanSaveAsPreset(PlaybackMode mode, Core.Model.Preset? preset, bool hasSelection) =>
        mode == PlaybackMode.Preset && preset is { IsDefault: true } && hasSelection ? Visibility.Visible : Visibility.Collapsed;
}
