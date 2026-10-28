using System.Text.Json.Serialization;

namespace Blanky.Core.Model;

// Persisted records use `set` rather than `init`: the System.Text.Json source
// generator assigns every init-only property while constructing, so a field
// missing from an older or hand-edited file would lose its default (volume 0,
// null lists). With setters it assigns only what the file contains.

/// <summary>Attribution for a sound's recording.</summary>
public sealed record SoundCredit
{
    public string? SoundName { get; set; }
    public string Author { get; set; } = "";
    public string? AuthorUrl { get; set; }
    public string? SourceUrl { get; set; }
    public string License { get; set; } = "";
}

/// <summary>A sound shipped with the app, from Assets/sounds.json.</summary>
public sealed record BuiltInSound
{
    public required string FileName { get; set; }
    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public required string Icon { get; set; }
    public bool IsMusic { get; set; }
    public double Duration { get; set; }
    public double Lufs { get; set; }
    public float NormalizationFactor { get; set; } = 1f;
    public SoundCredit? Credit { get; set; }
}

public sealed record SoundCatalogFile(IReadOnlyList<BuiltInSound> Sounds);

/// <summary>An audio file the user imported.</summary>
public sealed record CustomSound
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable key shared with presets, e.g. <c>custom-3f2a…</c>.</summary>
    public required string FileName { get; set; }

    /// <summary>Stored file name inside the custom sounds folder, with extension.</summary>
    public required string StoredFile { get; set; }

    public required string Title { get; set; }
    public string Icon { get; set; } = "music_note_1";
    public string? OriginalFileName { get; set; }
    public double? Lufs { get; set; }
    public float NormalizationFactor { get; set; } = 1f;
    public double Duration { get; set; }
    public string? Sha256 { get; set; }
    public SoundCredit? Credit { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Per-sound overrides, keyed by file name. Null means "use the default".</summary>
public sealed record SoundCustomization
{
    public string? Title { get; set; }
    public string? Icon { get; set; }
    public bool? IsHidden { get; set; }
    public bool? Loop { get; set; }
    public bool? Fade { get; set; }
    public bool? IsMusic { get; set; }
    public bool? IsPresetUseOnly { get; set; }
    public bool? RandomizeStart { get; set; }
    public bool? Normalize { get; set; }

    /// <summary>Manual gain used when normalization is off (0.5 = -50%, 1 = unchanged, 1.5 = +50%).</summary>
    public float? VolumeAdjustment { get; set; }

    [JsonIgnore]
    public bool IsEmpty => this == new SoundCustomization();
}

/// <summary>One sound's state inside a preset. Same shape as Blankie's <c>PresetState</c>.</summary>
public sealed record PresetState(string FileName, bool IsSelected, float Volume);

/// <summary>A saved mix. Field names match Blankie's <c>Preset</c>, so archives move between platforms.</summary>
public sealed record Preset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public IReadOnlyList<PresetState> SoundStates { get; set; } = [];
    public bool IsDefault { get; set; }
    public string? CreatedVersion { get; set; }
    public string? LastModifiedVersion { get; set; }
    public IReadOnlyList<string>? SoundOrder { get; set; }
    public string? CreatorName { get; set; }

    /// <summary>Names the artwork image in the artwork folder (<c>{id}.jpg</c>).</summary>
    public Guid? ArtworkId { get; set; }

    public int? Order { get; set; }
    public bool? IsImported { get; set; }
    public Guid? OriginalId { get; set; }

    /// <summary>One of <see cref="AccentColors"/> names, or null to follow the app accent.</summary>
    public string? AccentColorName { get; set; }

    /// <summary>The file names this preset includes, in display order.</summary>
    [JsonIgnore]
    public IEnumerable<string> SoundFileNames => SoundOrder is { Count: > 0 } order
        ? order.Where(f => SoundStates.Any(s => s.FileName == f)).Concat(SoundStates.Select(s => s.FileName).Except(order))
        : SoundStates.Select(s => s.FileName);
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum IconSize
{
    Small,
    Medium,
    Large,
}

/// <summary>Everything the user can set, persisted to settings.json.</summary>
public sealed record AppSettings
{
    public static readonly IReadOnlyList<string> DefaultQuickMixSounds =
        ["rain", "waves", "fireplace", "white-noise", "wind", "stream", "birds", "coffee-shop"];

    public double Volume { get; set; } = 1;
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Accent color name, or null for the Windows accent color.</summary>
    public string? AccentColorName { get; set; }

    public bool AutoPlayOnLaunch { get; set; }
    public bool ShowSoundNames { get; set; } = true;
    public IconSize IconSize { get; set; } = IconSize.Medium;
    public bool ShowProgressBorder { get; set; } = true;
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Closing the window keeps sounds playing from the tray, like a Mac app that stays in the menu bar.</summary>
    public bool CloseToTray { get; set; } = true;

    public bool LaunchAtStartup { get; set; }
    public bool SidebarVisible { get; set; } = true;
    public IReadOnlyList<string> StarredItems { get; set; } = [];
    public IReadOnlyList<string> QuickMixSoundFileNames { get; set; } = DefaultQuickMixSounds;
    public IReadOnlyList<string> SoundOrder { get; set; } = [];
    public Guid? LastPresetId { get; set; }
    public string? SingleSoundFileName { get; set; }
    public bool HasLaunchedBefore { get; set; }
    public WindowPlacement? Window { get; set; }
}

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);
