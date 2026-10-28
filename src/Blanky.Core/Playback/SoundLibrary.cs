using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Blanky.Core.Audio;
using Blanky.Core.Model;
using Blanky.Core.Storage;

namespace Blanky.Core.Playback;

/// <summary>
/// Every sound the user can play: the bundled catalog plus imports, in the user's
/// order, with their customizations applied.
/// </summary>
public sealed class SoundLibrary
{
    /// <summary>Formats Windows Media Foundation decodes (Ogg and Opus need the free Web Media Extensions).</summary>
    public static readonly IReadOnlyList<string> ImportExtensions =
        [".m4a", ".mp3", ".wav", ".flac", ".aac", ".wma", ".mp4", ".ogg", ".opus"];

    /// <summary>Longest stretch analysed for loudness. Longer files are still imported whole.</summary>
    private static readonly TimeSpan AnalysisLimit = TimeSpan.FromMinutes(20);

    private readonly DataStore _store;
    private readonly SettingsService _settings;
    private readonly IAudioDecoderFactory _decoders;
    private readonly Dictionary<string, SoundCustomization> _customizations;
    private readonly List<CustomSound> _customSounds;

    public SoundLibrary(DataStore store, SettingsService settings, IAudioDecoderFactory decoders, string catalogPath, string builtInDirectory)
    {
        _store = store;
        _settings = settings;
        _decoders = decoders;
        _customizations = store.LoadCustomizations();
        _customSounds = store.LoadCustomSounds();

        var items = new List<SoundItem>();
        foreach (var builtIn in DataStore.LoadCatalog(catalogPath))
        {
            items.Add(new SoundItem(builtIn, Path.Combine(builtInDirectory, builtIn.FileName + ".m4a")));
        }
        foreach (var custom in _customSounds)
        {
            items.Add(new SoundItem(custom, Path.Combine(store.Paths.CustomSoundsDirectory, custom.StoredFile)));
        }
        foreach (var item in items)
        {
            item.Customization = _customizations.GetValueOrDefault(item.FileName) ?? new SoundCustomization();
        }

        // Saved order first; anything new (e.g. a sound added in an update) keeps catalog order at the end.
        var rank = settings.Current.SoundOrder
            .Select((fileName, index) => (fileName, index))
            .DistinctBy(x => x.fileName)
            .ToDictionary(x => x.fileName, x => x.index);
        foreach (var item in items.OrderBy(i => rank.TryGetValue(i.FileName, out var n) ? n : rank.Count + items.IndexOf(i)))
        {
            Sounds.Add(item);
        }
    }

    public ObservableCollection<SoundItem> Sounds { get; } = [];

    /// <summary>Raised after a custom sound is deleted, so playback and presets can let go of it.</summary>
    public event EventHandler<SoundItem>? SoundRemoved;

    /// <summary>Raised after a customization changes how a sound plays (gain, loop, fade).</summary>
    public event EventHandler<SoundItem>? PlaybackOptionsChanged;

    public SoundItem? Find(string fileName) => Sounds.FirstOrDefault(s => s.FileName == fileName);

    public void Customize(SoundItem sound, Func<SoundCustomization, SoundCustomization> change)
    {
        var updated = change(sound.Customization);
        if (updated.IsEmpty)
        {
            _customizations.Remove(sound.FileName);
        }
        else
        {
            _customizations[sound.FileName] = updated;
        }
        sound.Customization = updated;
        _store.SaveCustomizations(_customizations);
        PlaybackOptionsChanged?.Invoke(this, sound);
    }

    public void ResetCustomization(SoundItem sound) => Customize(sound, _ => new SoundCustomization());

    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Sounds.Count || to >= Sounds.Count)
        {
            return;
        }
        Sounds.Move(from, to);
        SaveOrder();
    }

    /// <summary>Puts every sound back in catalog order (imports last, oldest first).</summary>
    public void ResetOrder()
    {
        var sorted = Sounds.OrderBy(s => s.IsCustom).ThenBy(s => s.Custom?.ImportedAt).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            Sounds.Move(Sounds.IndexOf(sorted[i]), i);
        }
        _settings.Update(s => s with { SoundOrder = [] });
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into the library, measures its loudness
    /// and adds it. Re-importing the same audio returns the existing sound.
    /// </summary>
    /// <exception cref="NotSupportedException">The file type isn't supported.</exception>
    /// <exception cref="InvalidDataException">The file couldn't be decoded or is silent.</exception>
    public async Task<SoundItem> ImportAsync(string sourcePath, string? title = null, string? icon = null, SoundCredit? credit = null, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!ImportExtensions.Contains(extension))
        {
            throw new NotSupportedException($"{AppInfo.Name} can't import {extension} files.");
        }

        var hash = await HashAsync(sourcePath, cancellationToken).ConfigureAwait(true);
        if (Sounds.FirstOrDefault(s => s.Custom?.Sha256 == hash) is { } existing)
        {
            return existing;
        }

        var id = Guid.NewGuid();
        var storedFile = $"{id:N}{extension}";
        var storedPath = Path.Combine(_store.Paths.CustomSoundsDirectory, storedFile);
        File.Copy(sourcePath, storedPath, overwrite: false);
        try
        {
            var (lufs, duration) = await Task.Run(() => Analyze(storedPath, cancellationToken), cancellationToken).ConfigureAwait(true);
            var custom = new CustomSound
            {
                Id = id,
                FileName = $"custom-{id:N}",
                StoredFile = storedFile,
                Title = string.IsNullOrWhiteSpace(title) ? TitleFromFileName(sourcePath) : title.Trim(),
                Icon = icon ?? "music_note_1",
                OriginalFileName = Path.GetFileName(sourcePath),
                Lufs = double.IsFinite(lufs) ? lufs : null,
                NormalizationFactor = Loudness.NormalizationFactor(lufs),
                Duration = duration.TotalSeconds,
                Sha256 = hash,
                Credit = credit,
            };
            _customSounds.Add(custom);
            _store.SaveCustomSounds(_customSounds);
            var item = new SoundItem(custom, storedPath);
            Sounds.Add(item);
            return item;
        }
        catch
        {
            File.Delete(storedPath);
            throw;
        }
    }

    public void UpdateCustom(SoundItem sound, string title, string icon, SoundCredit? credit)
    {
        if (sound.Custom is not { } custom)
        {
            return;
        }
        var updated = custom with { Title = title.Trim(), Icon = icon, Credit = credit };
        _customSounds[_customSounds.FindIndex(c => c.Id == custom.Id)] = updated;
        _store.SaveCustomSounds(_customSounds);
        sound.UpdateCustom(updated);
    }

    public void DeleteCustom(SoundItem sound)
    {
        if (sound.Custom is not { } custom)
        {
            return;
        }
        Sounds.Remove(sound);
        _customSounds.RemoveAll(c => c.Id == custom.Id);
        _customizations.Remove(sound.FileName);
        _store.SaveCustomSounds(_customSounds);
        _store.SaveCustomizations(_customizations);
        SaveOrder();
        SoundRemoved?.Invoke(this, sound);

        // The engine may still be fading the voice out; deleting later is fine if this fails.
        try
        {
            File.Delete(sound.FilePath);
        }
        catch (IOException)
        {
        }
    }

    private (double Lufs, TimeSpan Duration) Analyze(string path, CancellationToken cancellationToken)
    {
        // Count the whole file for duration, but cap the loudness pass for very long files.
        long frames = 0;
        double lufs;
        using (var decoder = new CountingDecoder(_decoders.Open(path, Mixer.SampleRate)))
        {
            lufs = Loudness.MeasureIntegratedLufs(decoder, AnalysisLimit, cancellationToken);
            frames = decoder.Frames;
            var buffer = new float[Mixer.SampleRate];
            int read;
            while ((read = decoder.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                frames += read / 2;
            }
        }
        if (frames < Mixer.SampleRate / 10)
        {
            throw new InvalidDataException("This file has no audio, or is too short to loop.");
        }
        return (lufs, TimeSpan.FromSeconds((double)frames / Mixer.SampleRate));
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    internal static string TitleFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
        return name.Length == 0 ? "Imported Sound" : char.ToUpper(name[0], System.Globalization.CultureInfo.CurrentCulture) + name[1..];
    }

    private void SaveOrder() => _settings.Update(s => s with { SoundOrder = [.. Sounds.Select(x => x.FileName)] });

    /// <summary>Counts frames read through it, so the loudness pass also yields the duration.</summary>
    private sealed class CountingDecoder(IAudioDecoder inner) : IAudioDecoder
    {
        public long Frames { get; private set; }

        public int Read(Span<float> buffer)
        {
            int read = inner.Read(buffer);
            Frames += read / 2;
            return read;
        }

        public void Dispose() => inner.Dispose();
    }
}
