using Blanky.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Blanky.Services;

/// <summary>
/// Recolors the accent brushes declared in Themes/Theme.xaml. Every Fluent control
/// templated on the accent (sliders, toggles, buttons) follows, as do Blanky's own
/// tiles. The accent is the active preset's color, else the user's choice, else the
/// Windows accent color.
/// </summary>
internal sealed class ThemeManager
{
    private readonly UISettings _uiSettings = new();
    private readonly ResourceDictionary _themeDictionaries;
    private string? _accentName;

    public ThemeManager(ResourceDictionary appResources)
    {
        // Found by its source file: in Release builds the merged dictionary's
        // ThemeDictionaries aren't populated until first used, so matching on
        // contents fails there.
        _themeDictionaries = appResources.MergedDictionaries.FirstOrDefault(
                d => d.Source?.OriginalString.EndsWith("Themes/Theme.xaml", StringComparison.OrdinalIgnoreCase) == true)
            ?? throw new InvalidOperationException(
                "Themes/Theme.xaml isn't merged into the app resources. Found: "
                + string.Join(", ", appResources.MergedDictionaries.Select(d => d.Source?.OriginalString ?? d.GetType().Name)));

        // Follow the Windows accent live when no named accent is chosen.
        _uiSettings.ColorValuesChanged += (_, _) => Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => ApplyAccent(_accentName));
    }

    public static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>The accent for the current theme, for code that draws its own visuals.</summary>
    public Color CurrentAccent(bool dark) => Resolve(_accentName, dark);

    public void ApplyAccent(string? accentName)
    {
        _accentName = accentName;
        Recolor("Light", Resolve(accentName, dark: false));
        Recolor("Dark", Resolve(accentName, dark: true));
    }

    private Color Resolve(string? accentName, bool dark)
    {
        if (AccentColors.Find(accentName) is { } named)
        {
            var rgb = dark ? named.Dark : named.Light;
            return Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
        }
        // The Windows accent, in the shade Windows itself uses on each theme.
        return _uiSettings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
    }

    private void Recolor(string theme, Color accent)
    {
        var dictionary = (ResourceDictionary)_themeDictionaries.ThemeDictionaries[theme];
        void Set(string key, byte alpha, Color? color = null)
        {
            var c = color ?? accent;
            ((SolidColorBrush)dictionary[key]).Color = Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        Set("AccentFillColorDefaultBrush", 255);
        Set("AccentFillColorSecondaryBrush", 230);
        Set("AccentFillColorTertiaryBrush", 204);
        Set("AccentTextFillColorPrimaryBrush", 255);
        Set("AccentTextFillColorSecondaryBrush", 255);
        Set("AccentTextFillColorTertiaryBrush", 255);
        Set("BlankyAccentBrush", 255);
        Set("BlankyAccentSoftBrush", theme == "Dark" ? (byte)0x38 : (byte)0x2E);
        Set("BlankyAccentGlowBrush", theme == "Dark" ? (byte)0x30 : (byte)0x24);

        // Pick black or white text on accent fills, whichever reads better.
        bool darkText = new Rgb(accent.R, accent.G, accent.B).PrefersDarkText;
        Set("TextOnAccentFillColorPrimaryBrush", 255, darkText ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255));
    }
}
