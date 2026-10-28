using System.ComponentModel;
using Blanky.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Blanky.Controls;

/// <summary>A sound in the mixer grid: tap the disc to toggle it, drag the slider to set its level.</summary>
/// <remarks>
/// Listens to its <see cref="SoundItem"/> only while loaded. The grid discards tiles
/// when the mix changes, and a discarded tile that kept listening would touch a XAML
/// element whose native half is gone (an E_UNEXPECTED crash). x:Bind tracking in a
/// UserControl doesn't stop on its own either, so it's stopped and resumed here too.
/// </remarks>
public sealed partial class SoundTile : UserControl
{
    public static readonly DependencyProperty SoundProperty = DependencyProperty.Register(
        nameof(Sound), typeof(SoundItem), typeof(SoundTile), new PropertyMetadata(null, OnSoundChanged));

    private SoundItem? _attached;
    private bool _isLoaded;

    public SoundTile()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public SoundItem? Sound
    {
        get => (SoundItem?)GetValue(SoundProperty);
        set => SetValue(SoundProperty, value);
    }

    private static void OnSoundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tile = (SoundTile)d;
        if (tile._isLoaded)
        {
            tile.Attach(e.NewValue as SoundItem);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        Bindings.Update();
        Attach(Sound);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        Bindings.StopTracking();
        Attach(null);
    }

    private void Attach(SoundItem? sound)
    {
        if (_attached is not null)
        {
            _attached.PropertyChanged -= OnSoundPropertyChanged;
        }
        _attached = sound;
        if (sound is not null)
        {
            sound.PropertyChanged += OnSoundPropertyChanged;
            Ring.Progress = sound.LoopProgress;
        }
        UpdateState(false);
    }

    private void OnSoundPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SoundItem.IsSelected))
        {
            UpdateState(true);
        }
        else if (e.PropertyName == nameof(SoundItem.LoopProgress) && sender is SoundItem sound)
        {
            Ring.Progress = sound.LoopProgress;
        }
    }

    private void UpdateState(bool animate) =>
        VisualStateManager.GoToState(this, _attached?.IsSelected == true ? "Selected" : "Idle", animate);

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (Sound is { } sound)
        {
            App.Current.Audio.ToggleOrResume(sound);
        }
    }
}
