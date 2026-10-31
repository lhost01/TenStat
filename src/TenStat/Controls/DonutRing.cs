using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace TenStat.Controls;

/// <summary>环形比例图（左/右键比例用），自绘，无依赖。</summary>
public sealed partial class DonutRing : UserControl
{
    private readonly Canvas _canvas = new();

    public DonutRing()
    {
        Content = _canvas;
        _canvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(DonutRing),
        new PropertyMetadata(0.0, OnInputChanged));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(DonutRing),
        new PropertyMetadata(16.0, OnInputChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(DonutRing),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(DonutRing),
        new PropertyMetadata(null, OnInputChanged));

    /// <summary>0~1 的比例。</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>环的粗细。注意：不能叫 Thickness，会和 UserControl.Thickness 撞名。</summary>
    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ValueBrush
    {
        get => (Brush?)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DonutRing ring) ring.Redraw();
    }

    private void Redraw()
    {
        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        if (width <= 4 || height <= 4) return;

        _canvas.Children.Clear();

        var thickness = Math.Clamp(RingThickness, 2, Math.Min(width, height) / 2 - 2);
        var size = Math.Min(width, height) - thickness;
        var radius = size / 2;
        var center = new Point(width / 2, height / 2);

        var track = TrackBrush ?? ResolveBrush("CardStrokeColorDefaultBrush", Color.FromArgb(60, 128, 128, 128));
        var accent = ValueBrush ?? ResolveBrush("AccentFillColorDefaultBrush", Colors.DodgerBlue);

        _canvas.Children.Add(new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = track,
            StrokeThickness = thickness,
            Margin = new Thickness(center.X - radius, center.Y - radius, 0, 0),
        });

        var ratio = Math.Clamp(Value, 0, 1);
        if (ratio <= 0.0005) return;

        // 从 12 点方向顺时针画
        const double startAngle = -Math.PI / 2;
        var sweep = ratio * Math.PI * 2;
        var endAngle = startAngle + sweep;

        Point PointAt(double angle) => new(
            center.X + radius * Math.Cos(angle),
            center.Y + radius * Math.Sin(angle));

        var figure = new PathFigure
        {
            StartPoint = PointAt(startAngle),
            IsClosed = false,
            IsFilled = false,
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = PointAt(endAngle),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweep > Math.PI,
        });

        _canvas.Children.Add(new Path
        {
            Data = new PathGeometry { Figures = { figure } },
            Stroke = accent,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
    }

    private static Brush ResolveBrush(string key, Color fallback)
    {
        try
        {
            if (Application.Current?.Resources is { } resources
                && resources.TryGetValue(key, out var value)
                && value is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
            // 资源还没加载完，用兜底色
        }

        return new SolidColorBrush(fallback);
    }
}
