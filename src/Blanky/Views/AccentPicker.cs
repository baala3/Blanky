using Blanky.Core.Model;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Blanky.Views;

/// <summary>Grid of accent swatches: the Windows accent plus Blankie's twelve named colors.</summary>
public sealed partial class AccentPicker : UserControl
{
    private readonly GridView _grid = new()
    {
        SelectionMode = ListViewSelectionMode.Single,
        IsItemClickEnabled = true,
        Width = 236,
    };

    public AccentPicker()
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Accent Color", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(_grid);
        _grid.ItemClick += OnItemClick;
        Content = panel;

        var system = new UISettings().GetColorValue(UIColorType.Accent);
        _grid.Items.Add(Swatch(null, "Windows accent", system));
        foreach (var accent in AccentColors.All)
        {
            _grid.Items.Add(Swatch(accent.Name, accent.DisplayName, Color.FromArgb(255, accent.Dark.R, accent.Dark.G, accent.Dark.B)));
        }
        Loaded += (_, _) => SelectCurrent();
    }

    /// <summary>Raised with the chosen accent name (null = Windows accent).</summary>
    public event EventHandler<string?>? AccentChosen;

    public void SelectCurrent()
    {
        var current = App.Current.Settings.Current.AccentColorName;
        _grid.SelectedItem = _grid.Items.OfType<FrameworkElement>().FirstOrDefault(i => (string?)i.Tag == current);
    }

    private static Grid Swatch(string? name, string label, Color color)
    {
        var swatch = new Grid { Width = 28, Height = 28, Tag = name };
        swatch.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new SolidColorBrush(color) });
        if (name is null)
        {
            // Mark the "follow Windows" option so it reads differently from a fixed color.
            swatch.Children.Add(new FontIcon { Glyph = "\uE770", FontSize = 13, Foreground = new SolidColorBrush(Colors.White) });
        }
        ToolTipService.SetToolTip(swatch, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, label);
        return swatch;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FrameworkElement item)
        {
            AccentChosen?.Invoke(this, (string?)item.Tag);
        }
    }
}
