using System.Collections.ObjectModel;
using System.ComponentModel;
using Blanky.Core.Model;
using Blanky.Core.Playback;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Blanky.Views;

/// <summary>One row in the sidebar: the all-sounds preset, Quick Mix, a preset or a sound.</summary>
public sealed partial class SidebarRow : ObservableObject
{
    public SidebarRow(PlayableItem item, string title, IReadOnlyList<string> icons)
    {
        Item = item;
        Title = title;
        Icons = icons;
    }

    public PlayableItem Item { get; }

    public string Title { get; }

    /// <summary>One icon for a sound; up to four (a 2×2 montage) for a preset.</summary>
    public IReadOnlyList<string> Icons { get; }

    public string Icon => Icons.Count > 0 ? Icons[0] : "music_note_1";

    public bool IsMontage => Icons.Count > 1;

    public bool IsSingleIcon => Icons.Count <= 1;

    public string? Icon2 => Icons.Count > 1 ? Icons[1] : null;

    public string? Icon3 => Icons.Count > 2 ? Icons[2] : null;

    public string? Icon4 => Icons.Count > 3 ? Icons[3] : null;

    public bool IsPreset => Item is PlayableItem.PresetItem;

    /// <summary>The all-sounds row shows the app icon, like Blankie's.</summary>
    public bool IsAppRow => Item is PlayableItem.AllSounds;

    public bool IsSound => Item is PlayableItem.SingleSound;

    [ObservableProperty]
    public partial bool IsStarred { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }
}

/// <summary>Builds the sidebar sections from the playback state and keeps them current.</summary>
public sealed class SidebarModel
{
    private readonly AudioManager _audio;
    private readonly SettingsService _settings;

    public SidebarModel(AudioManager audio, SettingsService settings)
    {
        _audio = audio;
        _settings = settings;
        audio.Presets.Presets.CollectionChanged += (_, _) =>
        {
            if (PresetSignature() != _presetSignature)
            {
                Rebuild();
            }
        };
        audio.Library.Sounds.CollectionChanged += (_, _) => Rebuild();
        audio.PropertyChanged += OnAudioChanged;
        settings.Changed += (_, e) =>
        {
            if (!e.Old.StarredItems.SequenceEqual(e.Current.StarredItems))
            {
                Rebuild();
            }
        };
        foreach (var sound in audio.Library.Sounds)
        {
            sound.PropertyChanged += OnSoundChanged;
        }
        audio.Library.Sounds.CollectionChanged += (_, e) =>
        {
            foreach (var added in e.NewItems?.OfType<SoundItem>() ?? [])
            {
                added.PropertyChanged += OnSoundChanged;
            }
        };
        Rebuild();
    }

    public ObservableCollection<SidebarRow> Favorites { get; } = [];

    public ObservableCollection<SidebarRow> Presets { get; } = [];

    public ObservableCollection<SidebarRow> Sounds { get; } = [];

    /// <summary>The all-sounds row, pinned above every section (a one-item list for the repeater).</summary>
    public ObservableCollection<SidebarRow> Pinned { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    public event EventHandler? Rebuilt;

    public void Rebuild()
    {
        _presetSignature = PresetSignature();
        var starred = _settings.Current.StarredItems.ToHashSet();
        Replace(Pinned, [RowFor(new PlayableItem.AllSounds())!]);

        // It can still be starred (so Ctrl+arrows cycle through it), but it's already pinned on top.
        Replace(Favorites, _audio.Favorites.Where(f => f is not PlayableItem.AllSounds).Select(RowFor).OfType<SidebarRow>());
        Replace(Presets, _audio.Presets.Presets.Where(p => !starred.Contains(new PlayableItem.PresetItem(p.Id).Token))
            .Select(p => RowFor(new PlayableItem.PresetItem(p.Id))).OfType<SidebarRow>());
        Replace(Sounds, _audio.Library.Sounds.Where(s => !s.IsHidden && !s.IsPresetUseOnly && !starred.Contains(new PlayableItem.SingleSound(s.FileName).Token))
            .Select(s => RowFor(new PlayableItem.SingleSound(s.FileName))).OfType<SidebarRow>());
        RefreshState();
        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    private SidebarRow? RowFor(PlayableItem item)
    {
        SidebarRow? row = item switch
        {
            PlayableItem.AllSounds => new SidebarRow(item, PresetManager.DefaultPresetName, ["sound_wave_circle"]),
            PlayableItem.QuickMix => new SidebarRow(item, "Quick Mix", ["sparkle"]),
            PlayableItem.PresetItem { Id: var id } when _audio.Presets.Find(id) is { } preset =>
                new SidebarRow(item, preset.Name, [.. preset.SoundFileNames.Select(_audio.Library.Find).OfType<SoundItem>().Take(4).Select(s => s.Icon)]),
            PlayableItem.SingleSound { FileName: var file } when _audio.Library.Find(file) is { } sound =>
                new SidebarRow(item, sound.Title, [sound.Icon]),
            _ => null,
        };
        if (row is not null)
        {
            row.IsStarred = _settings.IsStarred(item);
        }
        return row;
    }

    private void RefreshState()
    {
        var current = _audio.CurrentItem;
        foreach (var row in Pinned.Concat(Favorites).Concat(Presets).Concat(Sounds))
        {
            row.IsActive = row.Item == current;
            row.IsPlaying = row.IsActive && _audio.IsPlaying;
        }
    }

    private void OnAudioChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioManager.CurrentItem) or nameof(AudioManager.IsPlaying))
        {
            RefreshState();
        }
        else if (e.PropertyName is nameof(AudioManager.CurrentPreset) && PresetSignature() != _presetSignature)
        {
            // A preset's name or sound list (and so its montage) changed. Volume
            // saves also replace the preset but leave the signature alone.
            Rebuild();
        }
        else if (e.PropertyName is nameof(AudioManager.CurrentPreset))
        {
            RefreshState();
        }
    }

    private string _presetSignature = "";

    private string PresetSignature() => string.Join('|', _audio.Presets.Presets.Select(p => $"{p.Id}:{p.Name}:{string.Join(',', p.SoundFileNames)}"));

    private void OnSoundChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SoundItem.Title) or nameof(SoundItem.Icon) or nameof(SoundItem.IsHidden))
        {
            Rebuild();
        }
    }

    private static void Replace(ObservableCollection<SidebarRow> target, IEnumerable<SidebarRow> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }
}
