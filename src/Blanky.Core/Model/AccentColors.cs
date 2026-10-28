namespace Blanky.Core.Model;

/// <summary>An RGB color, independent of any UI framework.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(uint hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    /// <summary>Black or white, whichever reads better on this color.</summary>
    public bool PrefersDarkText => ((0.299 * R) + (0.587 * G) + (0.114 * B)) / 255 > 0.6;
}

/// <summary>A named accent with light and dark theme variants.</summary>
public sealed record AccentColor(string Name, string DisplayName, Rgb Light, Rgb Dark);

/// <summary>
/// The accent palette, in spectrum order. Names match Blankie's, so a preset's
/// accent survives a trip between platforms.
/// </summary>
public static class AccentColors
{
    public static IReadOnlyList<AccentColor> All { get; } =
    [
        new("red", "Red", Rgb.FromHex(0xFF3B30), Rgb.FromHex(0xFF453A)),
        new("pink", "Pink", Rgb.FromHex(0xFF2D55), Rgb.FromHex(0xFF375F)),
        new("orange", "Orange", Rgb.FromHex(0xFF9500), Rgb.FromHex(0xFF9F0A)),
        new("brown", "Brown", Rgb.FromHex(0xA2845E), Rgb.FromHex(0xAC8E68)),
        new("yellow", "Yellow", Rgb.FromHex(0xD4A600), Rgb.FromHex(0xFFD60A)),
        new("green", "Green", Rgb.FromHex(0x28A745), Rgb.FromHex(0x30D158)),
        new("mint", "Mint", Rgb.FromHex(0x00B3AA), Rgb.FromHex(0x63E6E2)),
        new("teal", "Teal", Rgb.FromHex(0x2A9DB5), Rgb.FromHex(0x40C8E0)),
        new("cyan", "Cyan", Rgb.FromHex(0x1E9BD7), Rgb.FromHex(0x64D2FF)),
        new("blue", "Blue", Rgb.FromHex(0x007AFF), Rgb.FromHex(0x0A84FF)),
        new("indigo", "Indigo", Rgb.FromHex(0x5856D6), Rgb.FromHex(0x5E5CE6)),
        new("purple", "Purple", Rgb.FromHex(0xAF52DE), Rgb.FromHex(0xBF5AF2)),
    ];

    public static AccentColor? Find(string? name) =>
        name is null ? null : All.FirstOrDefault(c => c.Name == name);
}
