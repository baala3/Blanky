using System.Text.Json;
using System.Text.Json.Serialization;
using Blanky.Core.Model;

namespace Blanky.Core.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SoundCatalogFile))]
[JsonSerializable(typeof(List<Preset>))]
[JsonSerializable(typeof(List<CustomSound>))]
[JsonSerializable(typeof(Dictionary<string, SoundCustomization>))]
internal sealed partial class BlankyJson : JsonSerializerContext;

/// <summary>Where Blanky keeps its data. Everything lives under one root, so tests can point it at a temp folder.</summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CustomSoundsDirectory);
        Directory.CreateDirectory(ArtworkDirectory);
    }

    /// <summary>%LOCALAPPDATA%\Blanky.</summary>
    public static AppPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name));

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string PresetsFile => Path.Combine(Root, "presets.json");
    public string CustomSoundsFile => Path.Combine(Root, "custom-sounds.json");
    public string CustomizationsFile => Path.Combine(Root, "sound-customizations.json");
    public string CustomSoundsDirectory => Path.Combine(Root, "Sounds");
    public string ArtworkDirectory => Path.Combine(Root, "Artwork");
    public string LogFile => Path.Combine(Root, "blanky.log");

    public string ArtworkPath(Guid artworkId) => Path.Combine(ArtworkDirectory, $"{artworkId:D}.jpg");
}

/// <summary>Reads and atomically writes one JSON document.</summary>
internal static class JsonFile
{
    public static T Load<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback();
            }
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, type) ?? fallback();
        }
        catch (JsonException ex)
        {
            // Keep the unreadable file for recovery instead of overwriting it on the next save.
            var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(path, backup, overwrite: true);
            System.Diagnostics.Trace.TraceError($"Could not read {path} ({ex.Message}); saved a copy to {backup}");
            return fallback();
        }
    }

    // Write to a sibling temp file, then swap it in, so a crash mid-write never
    // leaves a truncated document behind.
    public static void Save<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, value, type);
        }
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Typed access to each persisted document.</summary>
public sealed class DataStore(AppPaths paths)
{
    public AppPaths Paths { get; } = paths;

    public AppSettings LoadSettings() => JsonFile.Load(Paths.SettingsFile, BlankyJson.Default.AppSettings, () => new AppSettings());

    public void SaveSettings(AppSettings settings) => JsonFile.Save(Paths.SettingsFile, settings, BlankyJson.Default.AppSettings);

    public List<Preset> LoadPresets() => JsonFile.Load(Paths.PresetsFile, BlankyJson.Default.ListPreset, () => []);

    public void SavePresets(IEnumerable<Preset> presets) => JsonFile.Save(Paths.PresetsFile, presets.ToList(), BlankyJson.Default.ListPreset);

    public List<CustomSound> LoadCustomSounds() => JsonFile.Load(Paths.CustomSoundsFile, BlankyJson.Default.ListCustomSound, () => []);

    public void SaveCustomSounds(IEnumerable<CustomSound> sounds) => JsonFile.Save(Paths.CustomSoundsFile, sounds.ToList(), BlankyJson.Default.ListCustomSound);

    public Dictionary<string, SoundCustomization> LoadCustomizations() =>
        JsonFile.Load(Paths.CustomizationsFile, BlankyJson.Default.DictionaryStringSoundCustomization, () => []);

    public void SaveCustomizations(Dictionary<string, SoundCustomization> customizations) =>
        JsonFile.Save(Paths.CustomizationsFile, customizations, BlankyJson.Default.DictionaryStringSoundCustomization);

    public static IReadOnlyList<BuiltInSound> LoadCatalog(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, BlankyJson.Default.SoundCatalogFile)?.Sounds
            ?? throw new InvalidDataException($"Empty sound catalog: {path}");
    }
}
