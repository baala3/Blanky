using System.IO.Compression;
using System.Text;
using Blanky.Core.Model;
using Blanky.Core.Presets;
using Blanky.Core.Storage;

namespace Blanky.Core.Tests;

public sealed class LibraryAndArchiveTests : IDisposable
{
    private readonly TestApp _app = new();

    public LibraryAndArchiveTests() => _app.Audio.Restore();

    [Fact]
    public async Task Import_measures_loudness_and_deduplicates_by_content()
    {
        var file = _app.MakeAudioFile("my_white-noise.mp3");

        var sound = await _app.Library.ImportAsync(file, cancellationToken: TestContext.Current.CancellationToken);
        var again = await _app.Library.ImportAsync(file, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(sound, again);
        Assert.Equal("My white noise", sound.Title);
        Assert.Equal(1, sound.Duration.TotalSeconds, 2);
        // The fake decoder plays a 0.1-amplitude sine (about -20 LUFS), so normalization pulls it down.
        Assert.InRange(sound.MeasuredNormalization, 0.4f, 0.5f);
        Assert.Contains(sound, _app.Audio.VisibleSounds);
        Assert.Single(_app.Store.LoadCustomSounds());
    }

    [Fact]
    public async Task Import_rejects_unsupported_files()
    {
        var file = _app.MakeAudioFile("notes.txt");
        await Assert.ThrowsAsync<NotSupportedException>(() => _app.Library.ImportAsync(file, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_custom_sound_removes_it_everywhere()
    {
        var sound = await _app.Library.ImportAsync(_app.MakeAudioFile("bell.wav"), cancellationToken: TestContext.Current.CancellationToken);
        var preset = _app.Audio.CreatePreset("Bells", [sound.FileName, "rain"]);

        _app.Library.DeleteCustom(sound);

        Assert.Null(_app.Library.Find(sound.FileName));
        Assert.Equal(["rain"], _app.Presets.Find(preset.Id)!.SoundStates.Select(s => s.FileName));
        Assert.False(File.Exists(sound.FilePath));
    }

    [Fact]
    public async Task Archive_round_trip_keeps_the_mix_custom_sounds_artwork_and_color()
    {
        var bell = await _app.Library.ImportAsync(_app.MakeAudioFile("bell.wav"), "Bell", "sparkle", cancellationToken: TestContext.Current.CancellationToken);
        bell.Volume = 0.4f;
        var preset = _app.Audio.CreatePreset("Temple", [bell.FileName, "rain"], accentColorName: "purple", creatorName: "Bala");
        var artworkId = Guid.NewGuid();
        File.WriteAllBytes(_app.Paths.ArtworkPath(artworkId), [0xFF, 0xD8, 0xFF]);
        preset = _app.Audio.UpdatePreset(preset with { ArtworkId = artworkId });
        var archivePath = Path.Combine(_app.Root.FullName, "Temple.blanky");

        await new PresetArchive(_app.Audio, _app.Paths).ExportAsync(preset, archivePath, TestContext.Current.CancellationToken);

        using var other = new TestApp();
        try
        {
            var imported = await new PresetArchive(other.Audio, other.Paths).ImportAsync(archivePath, TestContext.Current.CancellationToken);

            Assert.Equal("Temple", imported.Name);
            Assert.Equal("purple", imported.AccentColorName);
            Assert.Equal("Bala", imported.CreatorName);
            Assert.Equal(preset.Id, imported.OriginalId);
            var importedBell = other.Library.Sounds.Single(s => s.IsCustom);
            Assert.Equal("Bell", importedBell.Title);
            Assert.Equal("sparkle", importedBell.Icon);
            Assert.Equal([importedBell.FileName, "rain"], imported.SoundStates.Select(s => s.FileName));
            Assert.Equal(0.4f, imported.SoundStates[0].Volume);
            Assert.True(File.Exists(other.Paths.ArtworkPath(imported.ArtworkId!.Value)));
        }
        finally
        {
            other.Dispose();
            other.DeleteRoot();
        }
    }

    [Fact]
    public async Task Imports_a_preset_written_by_Blankie_on_a_Mac()
    {
        // Shape produced by Blankie's PresetExporter (Swift JSONEncoder defaults).
        const string presetJson = """
            {"id":"5C2B6A0E-1C1E-4E0F-9E59-3D1E8C3B1F11","name":"Tranquil Waves","soundStates":[
              {"fileName":"waves","isSelected":true,"volume":0.6},
              {"fileName":"ambient-music","isSelected":true,"volume":0.8},
              {"fileName":"rain","isSelected":false,"volume":0.5}],
             "isDefault":false,"createdVersion":"2.1.0","creatorName":"Blankie Mixes",
             "accentColorName":"brown","moods":["relax"],"viewMode":"grid",
             "animatedArtwork":{"source":"bundled","bundledIdentifier":"swirl"}}
            """;
        var path = Path.Combine(_app.Root.FullName, "Tranquil Waves.blankie");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", """{"version":"1.0","blankieVersion":"2.1.0","createdDate":783302400,"compatibility":{"minimumBlankieVersion":"2.0.0","requiredFeatures":[]}}""");
            Write(zip, "preset.json", presetJson);
        }

        var imported = await new PresetArchive(_app.Audio, _app.Paths).ImportAsync(path, TestContext.Current.CancellationToken);
        _app.Audio.ApplyPreset(imported);

        Assert.Equal("Tranquil Waves", imported.Name);
        Assert.Equal("brown", imported.AccentColorName);
        Assert.Equal(["waves", "ambient-music", "rain"], _app.Audio.VisibleSounds.Select(s => s.FileName));
        Assert.Equal(["ambient-music", "waves"], _app.Player.Playing);
    }

    [Fact]
    public async Task Rejects_a_zip_that_is_not_a_preset()
    {
        var path = Path.Combine(_app.Root.FullName, "random.blanky");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(zip, "readme.txt", "hello");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => new PresetArchive(_app.Audio, _app.Paths).ImportAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Icon_map_translates_SF_symbols_both_ways()
    {
        Assert.Equal("weather_rain", IconMap.FromSymbol("cloud.rain"));
        Assert.Equal("weather_rain", IconMap.FromSymbol("cloud.rain.fill"));
        Assert.Equal(IconMap.FallbackIcon, IconMap.FromSymbol("something.unknown"));
        Assert.Equal("cloud.rain", IconMap.ToSymbol("weather_rain"));
    }

    [Fact]
    public void Every_built_in_sound_has_an_icon_and_credit()
    {
        var icons = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "icons.json")))
            .RootElement.GetProperty("icons");
        foreach (var sound in DataStore.LoadCatalog(TestApp.CatalogPath))
        {
            Assert.True(icons.TryGetProperty(sound.Icon, out _), $"{sound.FileName} uses missing icon {sound.Icon}");
            Assert.False(string.IsNullOrEmpty(sound.Credit?.Author), $"{sound.FileName} has no author");
            Assert.False(string.IsNullOrEmpty(sound.Credit?.License), $"{sound.FileName} has no license");
        }
    }

    [Fact]
    public void A_corrupt_settings_file_is_kept_aside_and_defaults_are_used()
    {
        File.WriteAllText(_app.Paths.SettingsFile, "{ not json");

        var settings = _app.Store.LoadSettings();

        Assert.Equal(new AppSettings(), settings);
        Assert.Single(Directory.GetFiles(_app.Paths.Root, "settings.json.corrupt-*"));
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }

    public void Dispose()
    {
        _app.Dispose();
        _app.DeleteRoot();
    }
}
