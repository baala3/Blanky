using System.Text.Json;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Blanky.Controls;

/// <summary>The icon set from Assets/icons.json (Fluent UI System Icons plus a few Material Symbols).</summary>
internal static class IconCatalog
{
    private static readonly Lazy<Data> Loaded = new(Load);

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Categories => Loaded.Value.Categories;

    public static bool Contains(string key) => Loaded.Value.Icons.ContainsKey(key);

    /// <summary>
    /// A new geometry for <paramref name="key"/>, mapped into a 0..24 square.
    /// Geometries can't be shared between elements, so every call builds one.
    /// </summary>
    public static Geometry? CreateGeometry(string? key, bool filled)
    {
        if (key is null || !Loaded.Value.Icons.TryGetValue(key, out var icon))
        {
            return null;
        }
        var geometry = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), filled ? icon.Filled : icon.Regular);
        double scale = 24 / icon.Size;
        if (scale != 1 || icon.MinX != 0 || icon.MinY != 0)
        {
            geometry.Transform = new MatrixTransform
            {
                Matrix = new Microsoft.UI.Xaml.Media.Matrix(scale, 0, 0, scale, -icon.MinX * scale, -icon.MinY * scale),
            };
        }
        return geometry;
    }

    private static Data Load()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "icons.json"));
        using var json = JsonDocument.Parse(stream);
        var icons = new Dictionary<string, Icon>();
        foreach (var property in json.RootElement.GetProperty("icons").EnumerateObject())
        {
            var box = property.Value.GetProperty("viewBox").GetString()!.Split(' ').Select(double.Parse).ToArray();
            icons[property.Name] = new Icon(
                property.Value.GetProperty("regular").GetString()!,
                property.Value.GetProperty("filled").GetString()!,
                box[0],
                box[1],
                Math.Max(box[2], box[3]));
        }
        var categories = json.RootElement.GetProperty("categories").EnumerateObject()
            .ToDictionary(c => c.Name, c => (IReadOnlyList<string>)[.. c.Value.EnumerateArray().Select(k => k.GetString()!)]);
        return new Data(icons, categories);
    }

    private sealed record Icon(string Regular, string Filled, double MinX, double MinY, double Size);

    private sealed record Data(Dictionary<string, Icon> Icons, IReadOnlyDictionary<string, IReadOnlyList<string>> Categories);
}
