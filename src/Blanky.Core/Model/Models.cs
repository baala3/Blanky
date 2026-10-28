using System.Text.Json.Serialization;

namespace Blanky.Core.Model;

/// <summary>Attribution for a sound's recording.</summary>
public sealed record SoundCredit
{
    public string? SoundName { get; init; }
    public string Author { get; init; } = "";
    public string? AuthorUrl { get; init; }
    public string? SourceUrl { get; init; }
    public string License { get; init; } = "";
}

/// <summary>A sound shipped with the app, from Assets/sounds.json.</summary>
public sealed record BuiltInSound
{
    public required string FileName { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string Icon { get; init; }
    public bool IsMusic { get; init; }
    public double Duration { get; init; }
    public double Lufs { get; init; }
    public float NormalizationFactor { get; init; } = 1f;
    public SoundCredit? Credit { get; init; }
}

public sealed record SoundCatalogFile(IReadOnlyList<BuiltInSound> Sounds);

/// <summary>An audio file the user imported.</summary>
public sealed record CustomSound
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Stable key shared with presets, e.g. <c>custom-3f2a…</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>Stored file name inside the custom sounds folder, with extension.</summary>
    public required string StoredFile { get; init; }

    public required string Title { get; init; }
    public string Icon { get; init; } = "music_note_1";
    public string? OriginalFileName { get; init; }
    public double? Lufs { get; init; }
    public float NormalizationFactor { get; init; } = 1f;
    public double Duration { get; init; }
    public string? Sha256 { get; init; }
    public SoundCredit? Credit { get; init; }
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Per-sound overrides, keyed by file name. Null means "use the default".</summary>
public sealed record SoundCustomization
{
    public string? Title { get; init; }
    public string? Icon { get; init; }
    public bool? IsHidden { get; init; }
    public bool? Loop { get; init; }
    public bool? Fade { get; init; }
    public bool? IsMusic { get; init; }
    public bool? IsPresetUseOnly { get; init; }
    public bool? RandomizeStart { get; init; }
    public bool? Normalize { get; init; }

    /// <summary>Manual gain used when normalization is off (0.5 = -50%, 1 = unchanged, 1.5 = +50%).</summary>
    public float? VolumeAdjustment { get; init; }

    [JsonIgnore]
    public bool IsEmpty => this == new SoundCustomization();
}

/// <summary>One sound's state inside a preset. Same shape as Blankie's <c>PresetState</c>.</summary>
public sealed record PresetState(string FileName, bool IsSelected, float Volume);

/// <summary>A saved mix. Field names match Blankie's <c>Preset</c>, so archives move between platforms.</summary>
public sealed record Preset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public IReadOnlyList<PresetState> SoundStates { get; init; } = [];
    public bool IsDefault { get; init; }
    public string? CreatedVersion { get; init; }
    public string? LastModifiedVersion { get; init; }
    public IReadOnlyList<string>? SoundOrder { get; init; }
    public string? CreatorName { get; init; }

    /// <summary>Names the artwork image in the artwork folder (<c>{id}.jpg</c>).</summary>
    public Guid? ArtworkId { get; init; }

    public int? Order { get; init; }
    public bool? IsImported { get; init; }
    public Guid? OriginalId { get; init; }

    /// <summary>One of <see cref="AccentColors"/> names, or null to follow the app accent.</summary>
    public string? AccentColorName { get; init; }

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

    public double Volume { get; init; } = 1;
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Accent color name, or null for the Windows accent color.</summary>
    public string? AccentColorName { get; init; }

    public bool AutoPlayOnLaunch { get; init; }
    public bool ShowSoundNames { get; init; } = true;
    public IconSize IconSize { get; init; } = IconSize.Medium;
    public bool ShowProgressBorder { get; init; } = true;
    public bool ShowTrayIcon { get; init; } = true;

    /// <summary>Closing the window keeps sounds playing from the tray, like a Mac app that stays in the menu bar.</summary>
    public bool CloseToTray { get; init; } = true;

    public bool LaunchAtStartup { get; init; }
    public bool SidebarVisible { get; init; } = true;
    public IReadOnlyList<string> StarredItems { get; init; } = [];
    public IReadOnlyList<string> QuickMixSoundFileNames { get; init; } = DefaultQuickMixSounds;
    public IReadOnlyList<string> SoundOrder { get; init; } = [];
    public Guid? LastPresetId { get; init; }
    public string? SingleSoundFileName { get; init; }
    public bool HasLaunchedBefore { get; init; }
    public WindowPlacement? Window { get; init; }
}

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);
