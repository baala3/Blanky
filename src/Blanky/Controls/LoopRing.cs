using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;

namespace Blanky.Controls;

/// <summary>A circular track with an arc showing how far through its loop a sound is.</summary>
public sealed partial class LoopRing : UserControl
{
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(LoopRing), new PropertyMetadata(4.0, (d, _) => ((LoopRing)d).UpdateArc()));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(LoopRing), new PropertyMetadata(null, (d, e) => ((LoopRing)d)._track.Stroke = (Brush)e.NewValue));

    public static readonly DependencyProperty ShowProgressProperty = DependencyProperty.Register(
        nameof(ShowProgress), typeof(bool), typeof(LoopRing), new PropertyMetadata(true, (d, _) => ((LoopRing)d).UpdateArc()));

    private readonly Ellipse _track = new();
    private readonly Path _arc = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    public LoopRing()
    {
        _arc.SetBinding(Shape.StrokeProperty, new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        Content = new Grid { Children = { _track, _arc } };
        SizeChanged += (_, _) => UpdateArc();
        IsTabStop = false;
    }

    private double _progress;

    /// <summary>Loop position, 0..1. Set from code ten times a second while playing.</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            if (value != _progress)
            {
                _progress = value;
                UpdateArc();
            }
        }
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>False draws the full accent ring with no progress arc (the "progress border" setting off).</summary>
    public bool ShowProgress
    {
        get => (bool)GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    private void UpdateArc()
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        _track.StrokeThickness = Thickness;
        _arc.StrokeThickness = Thickness;
        if (size <= Thickness)
        {
            _arc.Data = null;
            return;
        }

        double progress = ShowProgress ? Math.Clamp(Progress, 0, 1) : 1;
        double radius = (size - Thickness) / 2;
        var center = new Point(size / 2, size / 2);
        if (progress >= 0.9999)
        {
            _arc.Data = new EllipseGeometry { Center = center, RadiusX = radius, RadiusY = radius };
            return;
        }

        // Clockwise from 12 o'clock.
        double angle = progress * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + (radius * Math.Sin(angle)), center.Y - (radius * Math.Cos(angle)));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = angle > Math.PI,
            SweepDirection = SweepDirection.Clockwise,
        });
        _arc.Data = new PathGeometry { Figures = { figure } };
    }
}
