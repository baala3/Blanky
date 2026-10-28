namespace Blanky.Core.Presets;

/// <summary>
/// Translates between Blanky's icon keys and the SF Symbol names Blankie stores,
/// so imported sounds keep a sensible icon in both directions.
/// </summary>
public static class IconMap
{
    public const string FallbackIcon = "music_note_1";
    private const string FallbackSymbol = "waveform";

    private static readonly (string Icon, string Symbol)[] Pairs =
    [
        ("weather_rain", "cloud.rain"),
        ("weather_thunderstorm", "cloud.bolt.rain"),
        ("weather_squalls", "wind"),
        ("water", "water.waves"),
        ("weather_humidity", "humidity"),
        ("raven", "bird"),
        ("weather_moon", "moon.stars.fill"),
        ("vehicle_subway", "tram.fill"),
        ("vehicle_ship", "sailboat.fill"),
        ("city", "building.2"),
        ("drink_coffee", "cup.and.saucer.fill"),
        ("fireplace", "fireplace"),
        ("pulse", "waveform.path"),
        ("graphic_eq", "waveform"),
        ("sound_wave_circle", "waveform.low"),
        ("airwave", "waveform.mid"),
        ("sine_wave_dots", "pianokeys.inverse"),
        ("guitar", "guitars.fill"),
        ("mode_fan", "fan.desk"),
        ("music_note_2", "music.quarternote.3"),
        ("tree_deciduous", "tree.fill"),
        ("airplane", "airplane"),
        ("washer", "washer.fill"),
        ("piano", "pianokeys"),
        ("music_note_1", "music.note"),
        ("headphones", "headphones"),
        ("leaf_one", "leaf"),
        ("drop", "drop"),
        ("fire", "flame"),
        ("weather_fog", "cloud.fog"),
        ("beach", "beach.umbrella"),
        ("sparkle", "sparkles"),
        ("weather_sunny", "sun.max"),
        ("sleep", "zzz"),
        ("animal_paw_print", "pawprint"),
        ("tent", "tent"),
        ("weather_snowflake", "snowflake"),
        ("vehicle_car", "car.fill"),
        ("home", "house"),
        ("heart", "heart"),
        ("star", "star"),
    ];

    private static readonly Dictionary<string, string> BySymbol = Pairs
        .GroupBy(p => p.Symbol).ToDictionary(g => g.Key, g => g.First().Icon);

    private static readonly Dictionary<string, string> ByIcon = Pairs
        .GroupBy(p => p.Icon).ToDictionary(g => g.Key, g => g.First().Symbol);

    public static string FromSymbol(string? symbol)
    {
        if (symbol is null)
        {
            return FallbackIcon;
        }
        if (BySymbol.TryGetValue(symbol, out var icon))
        {
            return icon;
        }
        // "cloud.rain.fill" -> "cloud.rain", and so on.
        var trimmed = symbol.Replace(".fill", "", StringComparison.Ordinal).Replace(".circle", "", StringComparison.Ordinal);
        return BySymbol.GetValueOrDefault(trimmed, FallbackIcon);
    }

    public static string ToSymbol(string icon) => ByIcon.GetValueOrDefault(icon, FallbackSymbol);
}
