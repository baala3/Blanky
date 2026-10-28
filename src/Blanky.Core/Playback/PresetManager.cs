using System.Collections.ObjectModel;
using Blanky.Core.Model;
using Blanky.Core.Storage;

namespace Blanky.Core.Playback;

/// <summary>
/// Owns the saved presets: the built-in "All Blanky Sounds" preset (which tracks
/// the full library) and the user's own, in sidebar order. Every change is saved.
/// </summary>
public sealed class PresetManager
{
    public const string DefaultPresetName = "All " + AppInfo.Name + " Sounds";

    private readonly DataStore _store;
    private Preset _default;

    public PresetManager(DataStore store)
    {
        _store = store;
        var all = store.LoadPresets();
        _default = all.FirstOrDefault(p => p.IsDefault)
            ?? new Preset { Name = DefaultPresetName, IsDefault = true, CreatedVersion = AppInfo.Version };
        foreach (var preset in all.Where(p => !p.IsDefault).OrderBy(p => p.Order ?? int.MaxValue))
        {
            Presets.Add(preset);
        }
    }

    /// <summary>The always-present preset holding every sound's state.</summary>
    public Preset Default => _default;

    /// <summary>User presets, in sidebar order.</summary>
    public ObservableCollection<Preset> Presets { get; } = [];

    public Preset? Find(Guid id) => id == _default.Id ? _default : Presets.FirstOrDefault(p => p.Id == id);

    public Preset Create(string name, IEnumerable<PresetState> states, string? accentColorName = null, string? creatorName = null)
    {
        var preset = new Preset
        {
            Name = UniqueName(name.Trim()),
            SoundStates = [.. states],
            CreatedVersion = AppInfo.Version,
            LastModifiedVersion = AppInfo.Version,
            AccentColorName = accentColorName,
            CreatorName = string.IsNullOrWhiteSpace(creatorName) ? null : creatorName.Trim(),
        };
        Presets.Add(preset);
        Save();
        return preset;
    }

    /// <summary>Adds an imported preset as-is (archives keep their own id for duplicate detection).</summary>
    public Preset AddImported(Preset preset)
    {
        var added = preset with
        {
            Id = Guid.NewGuid(),
            Name = UniqueName(preset.Name),
            IsDefault = false,
            IsImported = true,
            OriginalId = preset.OriginalId ?? preset.Id,
        };
        Presets.Add(added);
        Save();
        return added;
    }

    /// <summary>Replaces the stored preset with the same id.</summary>
    public Preset Update(Preset preset)
    {
        var updated = preset with { LastModifiedVersion = AppInfo.Version };
        if (updated.IsDefault)
        {
            _default = updated;
        }
        else
        {
            int index = IndexOf(updated.Id);
            if (index < 0)
            {
                return updated;
            }
            Presets[index] = updated;
        }
        Save();
        return updated;
    }

    public void Delete(Guid id)
    {
        int index = IndexOf(id);
        if (index >= 0)
        {
            Presets.RemoveAt(index);
            Save();
        }
    }

    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Presets.Count || to >= Presets.Count)
        {
            return;
        }
        Presets.Move(from, to);
        Save();
    }

    /// <summary>"Name", or "Name 2", "Name 3"… if taken.</summary>
    public string UniqueName(string name, Guid? ignoring = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "New Preset";
        }
        bool Taken(string candidate) => Presets.Any(p => p.Id != ignoring && string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name))
        {
            return name;
        }
        for (int n = 2; ; n++)
        {
            var candidate = $"{name} {n}";
            if (!Taken(candidate))
            {
                return candidate;
            }
        }
    }

    public void Save()
    {
        var ordered = Presets.Select((p, i) => p with { Order = i });
        _store.SavePresets([_default, .. ordered]);
    }

    private int IndexOf(Guid id)
    {
        for (int i = 0; i < Presets.Count; i++)
        {
            if (Presets[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }
}
