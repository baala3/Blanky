namespace Blanky.Core.Playback;

/// <summary>
/// Something that can be starred and played from the Favorites list: the
/// all-sounds preset, Quick Mix, a user preset or a single sound. Persisted as a
/// short string token in settings.
/// </summary>
public abstract record PlayableItem
{
    private const string PresetPrefix = "preset:";
    private const string SoundPrefix = "sound:";

    public sealed record AllSounds : PlayableItem
    {
        public override string Token => "all-sounds";
    }

    public sealed record QuickMix : PlayableItem
    {
        public override string Token => "quick-mix";
    }

    public sealed record PresetItem(Guid Id) : PlayableItem
    {
        public override string Token => PresetPrefix + Id.ToString("D");
    }

    public sealed record SingleSound(string FileName) : PlayableItem
    {
        public override string Token => SoundPrefix + FileName;
    }

    public abstract string Token { get; }

    public static PlayableItem? Parse(string token) => token switch
    {
        "all-sounds" => new AllSounds(),
        "quick-mix" => new QuickMix(),
        _ when token.StartsWith(PresetPrefix, StringComparison.Ordinal) && Guid.TryParse(token[PresetPrefix.Length..], out var id) => new PresetItem(id),
        _ when token.StartsWith(SoundPrefix, StringComparison.Ordinal) => new SingleSound(token[SoundPrefix.Length..]),
        _ => null,
    };
}
