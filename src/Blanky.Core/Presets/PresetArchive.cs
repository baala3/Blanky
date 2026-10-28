using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blanky.Core.Model;
using Blanky.Core.Playback;

namespace Blanky.Core.Presets;

/// <summary>
/// Exports and imports presets as zip archives with the same layout as Blankie's
/// <c>.blankie</c> files (manifest.json, preset.json, artwork.jpg, sounds/), so a
/// mix can move between Windows and Apple devices.
/// </summary>
public sealed class PresetArchive(AudioManager audio, Storage.AppPaths paths)
{
    private const string ManifestEntry = "manifest.json";
    private const string PresetEntry = "preset.json";
    private const string ArtworkEntry = "artwork.jpg";
    private const string SoundsMetadataEntry = "sounds/metadata.json";
    private const long MaxEntryBytes = 1L << 30;

    // Swift's default Date coding: seconds since 2001-01-01 UTC.
    private static readonly DateTimeOffset SwiftEpoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task ExportAsync(Preset preset, string destination, CancellationToken cancellationToken = default)
    {
        var temp = destination + ".tmp";
        await using (var file = File.Create(temp))
        await using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var manifest = new ArchiveManifest
            {
                Version = "1.0",
                BlankieVersion = "2.0.0",
                BlankyVersion = AppInfo.Version,
                CreatedDate = (DateTimeOffset.UtcNow - SwiftEpoch).TotalSeconds,
                Compatibility = new ArchiveCompatibility { MinimumBlankieVersion = "2.0.0", RequiredFeatures = [] },
            };
            await WriteJsonAsync(zip, ManifestEntry, manifest, ArchiveJson.Default.ArchiveManifest, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(zip, PresetEntry, preset with { ArtworkId = null }, ArchiveJson.Default.Preset, cancellationToken).ConfigureAwait(false);

            if (preset.ArtworkId is { } artworkId && File.Exists(paths.ArtworkPath(artworkId)))
            {
                await zip.CreateEntryFromFileAsync(paths.ArtworkPath(artworkId), ArtworkEntry, CompressionLevel.NoCompression, cancellationToken).ConfigureAwait(false);
            }

            var customs = preset.SoundStates
                .Select(s => audio.Library.Find(s.FileName))
                .Where(s => s?.Custom is not null)
                .Select(s => s!)
                .ToList();
            if (customs.Count > 0)
            {
                var metadata = new SoundsManifest
                {
                    CustomSounds = [.. customs.Select(s => new CustomSoundMetadata
                    {
                        Id = s.Custom!.Id,
                        FileName = s.FileName + Path.GetExtension(s.Custom.StoredFile),
                        OriginalFileName = s.Custom.OriginalFileName ?? s.Title,
                        Title = s.Title,
                        SystemIconName = IconMap.ToSymbol(s.Icon),
                        BlankyIcon = s.Icon,
                        LufsValue = s.Custom.Lufs,
                        Sha256Hash = s.Custom.Sha256,
                        Credits = s.Credit is { } c ? new ArchiveCredits { SoundName = c.SoundName ?? s.Title, Author = c.Author, SourceUrl = c.SourceUrl, License = c.License } : null,
                    })],
                    BuiltInCustomizations = [],
                };
                await WriteJsonAsync(zip, SoundsMetadataEntry, metadata, ArchiveJson.Default.SoundsManifest, cancellationToken).ConfigureAwait(false);
                foreach (var sound in customs)
                {
                    var entry = "sounds/" + sound.FileName + Path.GetExtension(sound.Custom!.StoredFile);
                    await zip.CreateEntryFromFileAsync(sound.FilePath, entry, CompressionLevel.NoCompression, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        File.Move(temp, destination, overwrite: true);
    }

    /// <summary>Imports a .blanky or .blankie archive, adding any custom sounds it carries, and returns the new preset.</summary>
    /// <exception cref="InvalidDataException">The file isn't a preset archive.</exception>
    public async Task<Preset> ImportAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(archivePath);
        await using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        var preset = await ReadJsonAsync(zip, PresetEntry, ArchiveJson.Default.Preset, cancellationToken).ConfigureAwait(true)
            ?? throw new InvalidDataException("This file isn't a preset (preset.json is missing).");
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.SoundStates is null)
        {
            throw new InvalidDataException("This preset file is damaged.");
        }

        // Custom sounds: import each (deduplicated by content) and remap the preset to the new keys.
        var renames = new Dictionary<string, string>();
        var metadata = await ReadJsonAsync(zip, SoundsMetadataEntry, ArchiveJson.Default.SoundsManifest, cancellationToken).ConfigureAwait(true);
        var tempDir = Directory.CreateTempSubdirectory("blanky-import-");
        try
        {
            foreach (var custom in metadata?.CustomSounds ?? [])
            {
                var fileName = Path.GetFileName(custom.FileName);
                if (zip.GetEntry("sounds/" + fileName) is not { } entry || entry.Length > MaxEntryBytes)
                {
                    continue;
                }
                var extracted = Path.Combine(tempDir.FullName, Guid.NewGuid().ToString("N") + Path.GetExtension(fileName));
                await entry.ExtractToFileAsync(extracted, cancellationToken).ConfigureAwait(true);
                var credit = custom.Credits is { } c
                    ? new SoundCredit { SoundName = c.SoundName, Author = c.Author ?? "", SourceUrl = c.SourceUrl, License = c.License ?? "" }
                    : null;
                var sound = await audio.Library.ImportAsync(extracted, custom.Title, custom.BlankyIcon ?? IconMap.FromSymbol(custom.SystemIconName), credit, cancellationToken).ConfigureAwait(true);
                renames[Path.GetFileNameWithoutExtension(fileName)] = sound.FileName;
                renames[custom.Id.ToString()] = sound.FileName;
                renames[custom.Id.ToString().ToUpperInvariant()] = sound.FileName;
            }
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }

        string Map(string name) => renames.GetValueOrDefault(name, name);
        var states = preset.SoundStates
            .Select(s => s with { FileName = Map(s.FileName) })
            .Where(s => audio.Library.Find(s.FileName) is not null)
            .DistinctBy(s => s.FileName)
            .ToList();
        if (states.Count == 0)
        {
            throw new InvalidDataException("None of this preset's sounds are available.");
        }

        Guid? artworkId = null;
        if (zip.GetEntry(ArtworkEntry) is { Length: > 0 and < 50_000_000 } artwork)
        {
            artworkId = Guid.NewGuid();
            await artwork.ExtractToFileAsync(paths.ArtworkPath(artworkId.Value), cancellationToken).ConfigureAwait(true);
        }

        return audio.Presets.AddImported(preset with
        {
            SoundStates = states,
            SoundOrder = preset.SoundOrder?.Select(Map).ToList(),
            ArtworkId = artworkId,
            AccentColorName = AccentColors.Find(preset.AccentColorName)?.Name,
        });
    }

    private static async Task WriteJsonAsync<T>(ZipArchive zip, string name, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await JsonSerializer.SerializeAsync(stream, value, type, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonAsync<T>(ZipArchive zip, string name, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        if (zip.GetEntry(name) is not { } entry)
        {
            return null;
        }
        if (entry.Length > 10_000_000)
        {
            throw new InvalidDataException($"{name} is too large.");
        }
        try
        {
            await using var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, type, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{name} is damaged: {ex.Message}", ex);
        }
    }
}

internal sealed record ArchiveManifest
{
    public string Version { get; init; } = "1.0";
    public string BlankieVersion { get; init; } = "2.0.0";
    public string? BlankyVersion { get; init; }
    public double CreatedDate { get; init; }
    public ArchiveCompatibility Compatibility { get; init; } = new();
}

internal sealed record ArchiveCompatibility
{
    public string MinimumBlankieVersion { get; init; } = "2.0.0";
    public IReadOnlyList<string> RequiredFeatures { get; init; } = [];
}

internal sealed record SoundsManifest
{
    public IReadOnlyList<CustomSoundMetadata> CustomSounds { get; init; } = [];
    public IReadOnlyList<JsonElement> BuiltInCustomizations { get; init; } = [];
}

internal sealed record CustomSoundMetadata
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = "";
    public string OriginalFileName { get; init; } = "";
    public string Title { get; init; } = "";
    public string? SystemIconName { get; init; }

    /// <summary>Blanky's own icon key, so a Windows-to-Windows trip keeps the exact icon. Ignored by Blankie.</summary>
    public string? BlankyIcon { get; init; }

    public double? LufsValue { get; init; }
    public string? Sha256Hash { get; init; }
    public ArchiveCredits? Credits { get; init; }
}

internal sealed record ArchiveCredits
{
    public string? SoundName { get; init; }
    public string? Author { get; init; }
    public string? SourceUrl { get; init; }
    public string? License { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ArchiveManifest))]
[JsonSerializable(typeof(SoundsManifest))]
[JsonSerializable(typeof(Preset))]
internal sealed partial class ArchiveJson : JsonSerializerContext;
