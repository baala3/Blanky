using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Blanky.Core.Audio;
using Blanky.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Blanky.Core.Playback;

public enum PlaybackMode
{
    /// <summary>A preset (or the all-sounds preset) is in charge. Changes save into it.</summary>
    Preset,

    /// <summary>One sound plays on its own. Presets are left untouched.</summary>
    SingleSound,

    /// <summary>A scratch mix of a few favorite sounds. Nothing is saved.</summary>
    QuickMix,
}

/// <summary>
/// The playback authority, ported from Blankie's AudioManager and PresetManager
/// rules: which sounds are in the mix, whether playback is on, and which preset
/// or mode owns the mix. Lives on the UI thread.
/// </summary>
public sealed partial class AudioManager : ObservableObject, IDisposable
{
    public static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(0.5);
    public const float QuickMixVolume = 0.8f;

    private static readonly TimeSpan PresetSaveDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly ISoundPlayer _player;
    private readonly SettingsService _settings;
    private readonly SynchronizationContext? _context;
    private readonly ITimer _presetSaveTimer;
    private readonly ITimer _progressTimer;
    private Preset? _returnPreset;
    private bool _applying;
    private bool _presetSavePending;

    public AudioManager(ISoundPlayer player, SettingsService settings, PresetManager presets, SoundLibrary library, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        _player = player;
        _settings = settings;
        Presets = presets;
        Library = library;
        _context = SynchronizationContext.Current;
        SleepTimer = new SleepTimer(time);
        SleepTimer.Expired += (_, _) => SetPlaying(false);
        _presetSaveTimer = time.CreateTimer(_ => Post(SaveCurrentPresetState), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _progressTimer = time.CreateTimer(_ => Post(UpdateProgress), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _player.MasterVolume = (float)settings.Current.Volume;
        _player.VoiceFinished += OnVoiceFinished;
        _player.VoiceFailed += OnVoiceFailed;
        foreach (var sound in library.Sounds)
        {
            sound.PropertyChanged += OnSoundPropertyChanged;
        }
        library.Sounds.CollectionChanged += OnLibraryChanged;
        library.SoundRemoved += (_, sound) => OnSoundRemoved(sound);
        library.PlaybackOptionsChanged += (_, sound) => OnPlaybackOptionsChanged(sound);
        CurrentPreset = presets.Default;
    }

    public PresetManager Presets { get; }

    public SoundLibrary Library { get; }

    public SleepTimer SleepTimer { get; }

    /// <summary>The sounds the mixer shows for the current mode, in display order.</summary>
    public ObservableCollection<SoundItem> VisibleSounds { get; } = [];

    /// <summary>A sound couldn't be played (e.g. its file is damaged). It has been taken out of the mix.</summary>
    public event EventHandler<VoiceFailedEventArgs>? SoundFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentItem), nameof(NowPlayingTitle), nameof(AccentColorName))]
    public partial PlaybackMode Mode { get; private set; }

    /// <summary>The active preset. Kept during single sound (to return to it), null in Quick Mix.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentItem), nameof(NowPlayingTitle), nameof(AccentColorName), nameof(IsCustomPresetActive))]
    public partial Preset? CurrentPreset { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentItem), nameof(NowPlayingTitle))]
    public partial SoundItem? SingleSound { get; private set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingSubtitle))]
    public partial bool HasSelectedSounds { get; private set; }

    /// <summary>A user preset (not all-sounds) owns the mix, so it can be edited or exported.</summary>
    public bool IsCustomPresetActive => Mode == PlaybackMode.Preset && CurrentPreset is { IsDefault: false };

    /// <summary>The favorites-list item that matches what's playing.</summary>
    public PlayableItem CurrentItem => Mode switch
    {
        PlaybackMode.SingleSound when SingleSound is not null => new PlayableItem.SingleSound(SingleSound.FileName),
        PlaybackMode.QuickMix => new PlayableItem.QuickMix(),
        _ when CurrentPreset is { IsDefault: false } preset => new PlayableItem.PresetItem(preset.Id),
        _ => new PlayableItem.AllSounds(),
    };

    public string NowPlayingTitle => Mode switch
    {
        PlaybackMode.SingleSound when SingleSound is not null => SingleSound.Title,
        PlaybackMode.QuickMix => "Quick Mix",
        _ => CurrentPreset is { IsDefault: false } preset ? preset.Name : PresetManager.DefaultPresetName,
    };

    /// <summary>"Rain, Waves and Fireplace" style summary of the selected sounds.</summary>
    public string NowPlayingSubtitle
    {
        get
        {
            var titles = VisibleSounds.Where(s => s.IsSelected).Select(s => s.Title).ToList();
            return titles.Count switch
            {
                0 => "No sounds selected",
                1 => titles[0],
                <= 3 => string.Join(", ", titles[..^1]) + " and " + titles[^1],
                _ => $"{string.Join(", ", titles.Take(2))} and {titles.Count - 2} more",
            };
        }
    }

    /// <summary>Accent override from the active preset, if any.</summary>
    public string? AccentColorName => Mode == PlaybackMode.Preset ? CurrentPreset?.AccentColorName : null;

    /// <summary>Overall volume, 0..1.</summary>
    public double Volume
    {
        get => _settings.Current.Volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - Volume) < 0.0001)
            {
                return;
            }
            _player.MasterVolume = (float)clamped;
            _settings.Update(s => s with { Volume = clamped });
            OnPropertyChanged();
        }
    }

    /// <summary>Starred items that still exist, in starred order.</summary>
    public IReadOnlyList<PlayableItem> Favorites =>
        [.. _settings.Current.StarredItems.Select(PlayableItem.Parse).OfType<PlayableItem>().Where(Exists)];

    public bool CanNavigateFavorites
    {
        get
        {
            var favorites = Favorites;
            return favorites.Count > 1 || (favorites.Count == 1 && favorites[0] != CurrentItem);
        }
    }

    /// <summary>Restores the last preset or single sound, auto-playing if the user asked for that.</summary>
    public void Restore()
    {
        var settings = _settings.Current;
        var preset = settings.LastPresetId is { } id ? Presets.Find(id) : null;
        ApplyPreset(preset ?? Presets.Default, autoPlay: settings.AutoPlayOnLaunch, force: true);
        if (settings.SingleSoundFileName is { } single && Library.Find(single) is { } sound)
        {
            PlaySingleSound(sound, autoPlay: settings.AutoPlayOnLaunch);
        }
    }

    // ---- Playback ---------------------------------------------------------

    public void TogglePlayback() => SetPlaying(!IsPlaying);

    /// <summary>Starts or pauses every selected sound. Starting with nothing selected does nothing.</summary>
    public void SetPlaying(bool play)
    {
        if (play && !HasSelectedSounds)
        {
            return;
        }
        IsPlaying = play;
        foreach (var sound in VisibleSounds.Where(s => s.IsSelected))
        {
            if (play)
            {
                StartVoice(sound);
            }
            else
            {
                _player.Pause(sound.FileName, FadeFor(sound));
                sound.IsPlaying = false;
            }
        }
        _progressTimer.Change(play ? TimeSpan.Zero : Timeout.InfiniteTimeSpan, ProgressInterval);
    }

    /// <summary>
    /// A tap on a sound tile: while playing, toggles the sound in or out of the mix;
    /// while paused, adds it (if needed) and resumes everything.
    /// </summary>
    public void ToggleOrResume(SoundItem sound)
    {
        if (!VisibleSounds.Contains(sound))
        {
            return;
        }
        if (!IsPlaying)
        {
            SetSelected(sound, true);
            SetPlaying(true);
        }
        else
        {
            SetSelected(sound, !sound.IsSelected);
        }
    }

    public void SetSelected(SoundItem sound, bool selected)
    {
        if (sound.IsSelected == selected)
        {
            return;
        }
        sound.IsSelected = selected;
        if (selected && sound.IsMusic)
        {
            // One music track at a time, so two songs never clash.
            foreach (var other in VisibleSounds.Where(s => s != sound && s.IsSelected && s.IsMusic).ToList())
            {
                SetSelected(other, false);
            }
        }

        if (IsPlaying)
        {
            if (selected)
            {
                StartVoice(sound);
            }
            else
            {
                _player.Stop(sound.FileName, FadeFor(sound));
                sound.IsPlaying = false;
                sound.LoopProgress = 0;
            }
        }
        RefreshSelection();
        if (IsPlaying && !HasSelectedSounds)
        {
            SetPlaying(false);
        }
        SchedulePresetSave();
    }

    // ---- Presets ----------------------------------------------------------

    /// <summary>Makes <paramref name="preset"/> own the mix. Re-applying the active preset just resumes it.</summary>
    /// <param name="preset">The preset to switch to.</param>
    /// <param name="autoPlay">Start playback once the preset's sounds are in place.</param>
    /// <param name="force">Re-apply even if this preset is already active.</param>
    /// <param name="keepPlaying">If playback is on, keep it on with the new sounds (false pauses).</param>
    public void ApplyPreset(Preset preset, bool autoPlay = true, bool force = false, bool keepPlaying = true)
    {
        if (!force && Mode == PlaybackMode.Preset && CurrentPreset?.Id == preset.Id)
        {
            if (autoPlay && !IsPlaying)
            {
                SetPlaying(true);
            }
            return;
        }

        FlushPresetSave();
        bool play = autoPlay || (keepPlaying && IsPlaying);
        Mode = PlaybackMode.Preset;
        SingleSound = null;
        CurrentPreset = preset;
        _returnPreset = null;
        _settings.Update(s => s with { LastPresetId = preset.IsDefault ? null : preset.Id, SingleSoundFileName = null });
        ApplyStates(StatesFor(preset), play);
    }

    /// <summary>Saves the current mix as a new preset (the sounds in <paramref name="fileNames"/>) and switches to it.</summary>
    public Preset CreatePreset(string name, IEnumerable<string> fileNames, string? accentColorName = null, string? creatorName = null)
    {
        var states = fileNames
            .Select(Library.Find)
            .OfType<SoundItem>()
            .Select(s => new PresetState(s.FileName, true, (float)s.Volume))
            .ToList();
        var preset = Presets.Create(name, states, accentColorName, creatorName);
        ApplyPreset(preset, autoPlay: IsPlaying);
        return preset;
    }

    /// <summary>Saves edits to a preset (name, color, sound list…). Keeps the mix in step if it's the active one.</summary>
    public Preset UpdatePreset(Preset edited)
    {
        if (CurrentPreset?.Id == edited.Id && Mode == PlaybackMode.Preset)
        {
            FlushPresetSave();
        }
        var saved = Presets.Update(edited);
        if (CurrentPreset?.Id == saved.Id)
        {
            CurrentPreset = saved;
            if (Mode == PlaybackMode.Preset)
            {
                ApplyStates(StatesFor(saved), IsPlaying);
            }
        }
        return saved;
    }

    public void DeletePreset(Preset preset)
    {
        _settings.SetStarred(new PlayableItem.PresetItem(preset.Id), false);
        if (_returnPreset?.Id == preset.Id)
        {
            _returnPreset = null;
        }
        bool wasCurrent = CurrentPreset?.Id == preset.Id;
        Presets.Delete(preset.Id);
        if (wasCurrent)
        {
            if (Mode == PlaybackMode.Preset)
            {
                ApplyPreset(Presets.Default, autoPlay: IsPlaying, force: true);
            }
            else
            {
                CurrentPreset = Presets.Default;
            }
        }
        OnPropertyChanged(nameof(Favorites));
    }

    // ---- Single sound and Quick Mix ---------------------------------------

    /// <summary>Plays <paramref name="sound"/> on its own, leaving every preset as it was.</summary>
    public void PlaySingleSound(SoundItem sound, bool autoPlay = true)
    {
        if (Mode == PlaybackMode.SingleSound && SingleSound == sound)
        {
            if (autoPlay && !IsPlaying)
            {
                SetPlaying(true);
            }
            return;
        }

        FlushPresetSave();
        bool play = autoPlay || IsPlaying;
        if (Mode == PlaybackMode.QuickMix)
        {
            CurrentPreset = _returnPreset ?? Presets.Default;
        }
        Mode = PlaybackMode.SingleSound;
        SingleSound = sound;
        _settings.Update(s => s with { SingleSoundFileName = sound.FileName });
        ApplyStates([new PresetState(sound.FileName, true, (float)sound.Volume)], play);
    }

    /// <summary>Leaves single sound or Quick Mix and goes back to the preset that was active.</summary>
    public void ReturnToPreset()
    {
        if (Mode != PlaybackMode.Preset)
        {
            ApplyPreset(_returnPreset ?? CurrentPreset ?? Presets.Default, autoPlay: IsPlaying, force: true);
        }
    }

    public void EnterQuickMix()
    {
        if (Mode == PlaybackMode.QuickMix)
        {
            return;
        }
        FlushPresetSave();
        _returnPreset = CurrentPreset ?? Presets.Default;
        Mode = PlaybackMode.QuickMix;
        SingleSound = null;
        CurrentPreset = null;
        _settings.Update(s => s with { SingleSoundFileName = null });
        var states = _settings.Current.QuickMixSoundFileNames
            .Select(Library.Find)
            .OfType<SoundItem>()
            .Where(s => !s.IsPresetUseOnly)
            .Select(s => new PresetState(s.FileName, false, QuickMixVolume));
        ApplyStates([.. states], play: false);
    }

    // ---- Favorites --------------------------------------------------------

    public bool IsStarred(PlayableItem item) => _settings.IsStarred(item);

    public void SetStarred(PlayableItem item, bool starred)
    {
        _settings.SetStarred(item, starred);
        OnPropertyChanged(nameof(Favorites));
        OnPropertyChanged(nameof(CanNavigateFavorites));
    }

    public void Activate(PlayableItem item)
    {
        switch (item)
        {
            case PlayableItem.AllSounds:
                // The all-sounds page is a library to browse: opening it never starts sound.
                ApplyPreset(Presets.Default, autoPlay: false, keepPlaying: false);
                break;
            case PlayableItem.QuickMix:
                EnterQuickMix();
                break;
            case PlayableItem.PresetItem { Id: var id } when Presets.Find(id) is { } preset:
                ApplyPreset(preset);
                break;
            case PlayableItem.SingleSound { FileName: var file } when Library.Find(file) is { } sound:
                PlaySingleSound(sound);
                break;
        }
    }

    public void NextFavorite() => StepFavorite(+1);

    public void PreviousFavorite() => StepFavorite(-1);

    private void StepFavorite(int direction)
    {
        var favorites = Favorites;
        if (favorites.Count == 0)
        {
            return;
        }
        int index = favorites.ToList().IndexOf(CurrentItem);
        int next = index < 0
            ? (direction > 0 ? 0 : favorites.Count - 1)
            : (index + direction + favorites.Count) % favorites.Count;
        Activate(favorites[next]);
    }

    private bool Exists(PlayableItem item) => item switch
    {
        PlayableItem.PresetItem { Id: var id } => Presets.Find(id) is not null,
        PlayableItem.SingleSound { FileName: var file } => Library.Find(file) is not null,
        _ => true,
    };

    // ---- Mix state --------------------------------------------------------

    /// <summary>The states a preset asks for. The all-sounds preset covers the whole (non-hidden) library.</summary>
    private List<PresetState> StatesFor(Preset preset)
    {
        if (!preset.IsDefault)
        {
            var states = preset.SoundStates.ToDictionary(s => s.FileName);
            return [.. preset.SoundFileNames.Where(f => Library.Find(f) is not null).Select(f => states[f])];
        }
        var saved = preset.SoundStates.ToDictionary(s => s.FileName);
        return [.. Library.Sounds
            .Where(s => !s.IsHidden && !s.IsPresetUseOnly)
            .Select(s => saved.TryGetValue(s.FileName, out var state)
                ? state
                : new PresetState(s.FileName, false, (float)SoundItem.DefaultVolume))];
    }

    /// <summary>Makes the library match <paramref name="states"/>: those sounds become visible with the given selection and volume; everything else leaves the mix.</summary>
    private void ApplyStates(IReadOnlyList<PresetState> states, bool play)
    {
        _applying = true;
        try
        {
            var byFile = states.ToDictionary(s => s.FileName);
            foreach (var sound in Library.Sounds)
            {
                if (byFile.TryGetValue(sound.FileName, out var state))
                {
                    sound.Volume = Math.Clamp(state.Volume, 0, 1);
                    sound.IsSelected = state.IsSelected;
                }
                else if (sound.IsSelected || sound.IsPlaying)
                {
                    sound.IsSelected = false;
                }
            }

            // A preset saved with two music tracks keeps only the first.
            foreach (var extra in states.Where(s => s.IsSelected).Select(s => Library.Find(s.FileName)).OfType<SoundItem>().Where(s => s.IsMusic).Skip(1))
            {
                extra.IsSelected = false;
            }

            VisibleSounds.Clear();
            foreach (var state in states)
            {
                if (Library.Find(state.FileName) is { } sound)
                {
                    VisibleSounds.Add(sound);
                }
            }
            RefreshSelection();

            play &= HasSelectedSounds;
            IsPlaying = play;
            foreach (var sound in Library.Sounds)
            {
                if (sound.IsSelected && play)
                {
                    StartVoice(sound);
                }
                else if (sound.IsSelected)
                {
                    _player.Pause(sound.FileName, FadeFor(sound));
                    sound.IsPlaying = false;
                }
                else
                {
                    _player.Stop(sound.FileName, FadeFor(sound));
                    sound.IsPlaying = false;
                    sound.LoopProgress = 0;
                }
            }
            _progressTimer.Change(play ? TimeSpan.Zero : Timeout.InfiniteTimeSpan, ProgressInterval);
        }
        finally
        {
            _applying = false;
        }
        OnPropertyChanged(nameof(CanNavigateFavorites));
        OnPropertyChanged(nameof(NowPlayingSubtitle));
    }

    private void StartVoice(SoundItem sound)
    {
        _player.Play(sound.FileName, sound.FilePath, sound.EngineGain, FadeFor(sound), new VoiceOptions(sound.Loops, sound.Duration));
        sound.IsPlaying = true;
    }

    private static TimeSpan FadeFor(SoundItem sound) => sound.Fades ? FadeDuration : TimeSpan.Zero;

    private void RefreshSelection()
    {
        HasSelectedSounds = VisibleSounds.Any(s => s.IsSelected);
        OnPropertyChanged(nameof(NowPlayingSubtitle));
    }

    private void UpdateProgress()
    {
        foreach (var sound in VisibleSounds)
        {
            if (sound.IsPlaying)
            {
                sound.LoopProgress = _player.GetLoopProgress(sound.FileName) ?? 0;
            }
        }
    }

    // ---- Preset persistence ------------------------------------------------

    private void SchedulePresetSave()
    {
        if (_applying || Mode != PlaybackMode.Preset || CurrentPreset is null)
        {
            return;
        }
        _presetSavePending = true;
        _presetSaveTimer.Change(PresetSaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes any pending mix change into the active preset now.</summary>
    public void FlushPresetSave()
    {
        _presetSaveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        SaveCurrentPresetState();
    }

    private void SaveCurrentPresetState()
    {
        if (!_presetSavePending || Mode != PlaybackMode.Preset || CurrentPreset is not { } preset)
        {
            return;
        }
        _presetSavePending = false;

        List<PresetState> states;
        if (preset.IsDefault)
        {
            // Keep states for hidden sounds too, so unhiding one restores its last volume.
            var visible = Library.Sounds.Where(s => !s.IsPresetUseOnly).Select(s => new PresetState(s.FileName, s.IsSelected, (float)s.Volume));
            var kept = preset.SoundStates.Where(s => Library.Find(s.FileName) is null);
            states = [.. visible, .. kept];
        }
        else
        {
            states = [.. preset.SoundStates.Select(state => Library.Find(state.FileName) is { } sound
                ? new PresetState(sound.FileName, sound.IsSelected, (float)sound.Volume)
                : state)];
        }
        CurrentPreset = Presets.Update(preset with { SoundStates = states });
    }

    // ---- Events -----------------------------------------------------------

    private void OnSoundPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SoundItem sound && e.PropertyName == nameof(SoundItem.Volume))
        {
            _player.SetGain(sound.FileName, sound.EngineGain);
            SchedulePresetSave();
        }
    }

    private void OnLibraryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A Move lists the moved item in both NewItems and OldItems; it's already subscribed.
        if (e.Action != NotifyCollectionChangedAction.Move)
        {
            foreach (var removed in e.OldItems?.OfType<SoundItem>() ?? [])
            {
                removed.PropertyChanged -= OnSoundPropertyChanged;
            }
            foreach (var added in e.NewItems?.OfType<SoundItem>() ?? [])
            {
                added.PropertyChanged += OnSoundPropertyChanged;
            }
        }

        // New imports appear in the all-sounds preset right away; a reorder re-sorts the grid.
        if (Mode == PlaybackMode.Preset && CurrentPreset is { IsDefault: true } && !_applying)
        {
            FlushPresetSave();
            ApplyStates(StatesFor(Presets.Default), IsPlaying);
        }
    }

    private void OnSoundRemoved(SoundItem sound)
    {
        _player.Stop(sound.FileName, TimeSpan.Zero);
        SetStarred(new PlayableItem.SingleSound(sound.FileName), false);
        _settings.Update(s => s with { QuickMixSoundFileNames = [.. s.QuickMixSoundFileNames.Where(f => f != sound.FileName)] });
        foreach (var preset in Presets.Presets.Where(p => p.SoundStates.Any(s => s.FileName == sound.FileName)).ToList())
        {
            var updated = Presets.Update(preset with
            {
                SoundStates = [.. preset.SoundStates.Where(s => s.FileName != sound.FileName)],
                SoundOrder = preset.SoundOrder?.Where(f => f != sound.FileName).ToList(),
            });
            if (CurrentPreset?.Id == updated.Id)
            {
                CurrentPreset = updated;
            }
        }

        if (SingleSound == sound)
        {
            ApplyPreset(CurrentPreset ?? Presets.Default, autoPlay: false, force: true);
        }
        else if (VisibleSounds.Contains(sound))
        {
            ApplyStates([.. VisibleSounds.Where(s => s != sound).Select(s => new PresetState(s.FileName, s.IsSelected, (float)s.Volume))], IsPlaying);
        }
    }

    private void OnPlaybackOptionsChanged(SoundItem sound)
    {
        _player.SetGain(sound.FileName, sound.EngineGain);

        // Hiding a sound takes it out of the all-sounds grid; unhiding brings it back.
        if (Mode == PlaybackMode.Preset && CurrentPreset is { IsDefault: true })
        {
            FlushPresetSave();
            ApplyStates(StatesFor(Presets.Default), IsPlaying);
        }
        else if (sound.IsPlaying)
        {
            // Loop and fade options take effect from the next start.
            _player.Stop(sound.FileName, TimeSpan.Zero);
            StartVoice(sound);
        }
    }

    private void OnVoiceFinished(object? sender, string key)
    {
        if (_disposed)
        {
            return;
        }
        if (Library.Find(key) is { IsSelected: true } sound)
        {
            SetSelected(sound, false);
        }
    }

    private void OnVoiceFailed(object? sender, VoiceFailedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }
        if (Library.Find(e.Key) is { IsSelected: true } sound)
        {
            SetSelected(sound, false);
        }
        SoundFailed?.Invoke(this, e);
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
        FlushPresetSave();
        _disposed = true;
        _presetSaveTimer.Dispose();
        _progressTimer.Dispose();
        SleepTimer.Dispose();
    }
}
