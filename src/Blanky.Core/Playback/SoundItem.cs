using Blanky.Core.Audio;
using Blanky.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Blanky.Core.Playback;

/// <summary>
/// A playable sound (built-in or imported) as the UI sees it. Selection and play
/// state are owned by <see cref="AudioManager"/>; the UI writes only
/// <see cref="Volume"/> directly (slider binding).
/// </summary>
public sealed partial class SoundItem : ObservableObject
{
    public const double DefaultVolume = 0.75;

    private SoundCustomization _customization = new();

    internal SoundItem(BuiltInSound builtIn, string filePath)
    {
        FileName = builtIn.FileName;
        FilePath = filePath;
        DefaultTitle = builtIn.Title;
        DefaultIcon = builtIn.Icon;
        DefaultIsMusic = builtIn.IsMusic;
        Subtitle = builtIn.Subtitle;
        Duration = TimeSpan.FromSeconds(builtIn.Duration);
        MeasuredNormalization = builtIn.NormalizationFactor;
        Credit = builtIn.Credit;
    }

    internal SoundItem(CustomSound custom, string filePath)
    {
        FileName = custom.FileName;
        FilePath = filePath;
        Custom = custom;
        DefaultTitle = custom.Title;
        DefaultIcon = custom.Icon;
        Duration = TimeSpan.FromSeconds(custom.Duration);
        MeasuredNormalization = custom.NormalizationFactor;
        Credit = custom.Credit;
    }

    /// <summary>Stable identity, shared with presets (e.g. "rain" or "custom-…").</summary>
    public string FileName { get; }

    public string FilePath { get; }

    public CustomSound? Custom { get; private set; }

    public bool IsCustom => Custom is not null;

    public string DefaultTitle { get; private set; }

    public string DefaultIcon { get; private set; }

    public bool DefaultIsMusic { get; }

    public string? Subtitle { get; }

    public TimeSpan Duration { get; }

    public SoundCredit? Credit { get; }

    /// <summary>Gain from loudness analysis that brings this sound to the shared target.</summary>
    public float MeasuredNormalization { get; private set; }

    public SoundCustomization Customization
    {
        get => _customization;
        internal set
        {
            _customization = value;
            OnPropertyChanged(nameof(Customization));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(IsMusic));
            OnPropertyChanged(nameof(IsHidden));
        }
    }

    public string Title => _customization.Title ?? DefaultTitle;

    public string Icon => _customization.Icon ?? DefaultIcon;

    public bool IsMusic => _customization.IsMusic ?? DefaultIsMusic;

    public bool IsHidden => _customization.IsHidden ?? false;

    public bool IsPresetUseOnly => _customization.IsPresetUseOnly ?? false;

    public bool Loops => _customization.Loop ?? true;

    public bool Fades => _customization.Fade ?? true;

    /// <summary>Slider position, 0..1.</summary>
    [ObservableProperty]
    public partial double Volume { get; set; } = DefaultVolume;

    /// <summary>In the current mix (it plays whenever playback is on).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; internal set; }

    /// <summary>Selected and playback is on.</summary>
    [ObservableProperty]
    public partial bool IsPlaying { get; internal set; }

    /// <summary>Position in the current loop, 0..1, refreshed while playing.</summary>
    [ObservableProperty]
    public partial double LoopProgress { get; internal set; }

    /// <summary>Linear gain for the engine: volume curve times loudness correction, at most +24 dB.</summary>
    public float EngineGain
    {
        get
        {
            float correction = (_customization.Normalize ?? true)
                ? MeasuredNormalization
                : _customization.VolumeAdjustment ?? 1f;
            return Loudness.VolumeCurve(Volume) * Math.Min(correction, 15.85f);
        }
    }

    internal void UpdateCustom(CustomSound custom)
    {
        Custom = custom;
        DefaultTitle = custom.Title;
        DefaultIcon = custom.Icon;
        MeasuredNormalization = custom.NormalizationFactor;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Icon));
    }

    public override string ToString() => Title;
}
