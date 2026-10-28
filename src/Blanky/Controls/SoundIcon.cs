using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Blanky.Controls;

/// <summary>Draws an icon from <see cref="IconCatalog"/> in the control's Foreground, scaled to its size.</summary>
public sealed partial class SoundIcon : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(SoundIcon), new PropertyMetadata(null, (d, _) => ((SoundIcon)d).Rebuild()));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(SoundIcon), new PropertyMetadata(false, (d, _) => ((SoundIcon)d).Rebuild()));

    private readonly Path _path = new();

    public SoundIcon()
    {
        _path.SetBinding(Shape.FillProperty, new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = new Canvas { Width = 24, Height = 24, Children = { _path } },
        };
        IsTabStop = false;
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    private void Rebuild() => _path.Data = IconCatalog.CreateGeometry(Icon, Filled);
}
