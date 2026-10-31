using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace TenStat.Controls;

/// <summary>
/// 极简折线图（自绘，不依赖 LiveCharts2）。
/// 为什么不用图表库：这个场景只要“一眼看趋势”，自绘一百行、零依赖、打包小、不会被第三方版本坑。
/// </summary>
public sealed partial class MiniLineChart : UserControl
{
    private readonly Canvas _canvas = new();
    private bool _redrawQueued;

    public MiniLineChart()
    {
        Content = _canvas;
        _canvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    // Values 用 object 声明，绕开 DependencyProperty 注册泛型接口时的 WinRT 限制
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(object), typeof(MiniLineChart),
        new PropertyMetadata(null, OnChartInputChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(MiniLineChart),
        new PropertyMetadata(null, OnChartInputChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(MiniLineChart),
        new PropertyMetadata(null, OnChartInputChanged));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(MiniLineChart),
        new PropertyMetadata(true, OnChartInputChanged));

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty) as IReadOnlyList<double>;
        set => SetValue(ValuesProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    private static void OnChartInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MiniLineChart chart)
        {
            chart.RequestRedraw();
        }
    }

    /// <summary>
    /// 属性变化回调里不要直接改建子元素树：那时可能正在 XAML 绑定/布局中，
    /// 直接改会重入。这里抛到 UI 队列下一拍再画。
    /// </summary>
    private void RequestRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;

        var queue = DispatcherQueue;
        if (queue is null || !queue.TryEnqueue(() =>
            {
                _redrawQueued = false;
                Redraw();
            }))
        {
            _redrawQueued = false;
            Redraw();
        }
    }

    private void Redraw()
    {
        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        if (width <= 1 || height <= 1) return;

        _canvas.Children.Clear();

        var values = Values ?? Array.Empty<double>();
        if (values.Count == 0)
        {
            _canvas.Children.Add(new TextBlock
            {
                Text = "暂无数据",
                FontSize = 12,
                Foreground = ResolveBrush("TextFillColorTertiaryBrush", Colors.Gray),
            });
            return;
        }

        var lineBrush = Stroke ?? ResolveBrush("ChartLineBrush", Colors.DodgerBlue);
        var fillBrush = Fill ?? ResolveBrush("ChartFillBrush", Color.FromArgb(32, 15, 108, 189));

        if (ShowGrid)
        {
            for (var i = 1; i <= 2; i++)
            {
                var gridY = height * i / 3.0;
                _canvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = gridY,
                    X2 = width,
                    Y2 = gridY,
                    Stroke = ResolveBrush("CardStrokeColorDefaultBrush", Color.FromArgb(40, 128, 128, 128)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 4 },
                });
            }
        }

        var max = Math.Max(values.Max(), 1.0);
        var count = values.Count;
        var stepX = count == 1 ? width : width / (count - 1);

        // WinUI 的 PointCollection 不能同时挂在两个父对象上，所以面积和折线各建一份
        var linePoints = new PointCollection();
        var areaPoints = new PointCollection();
        for (var i = 0; i < count; i++)
        {
            var x = i * stepX;
            var y = height - (values[i] / max) * (height - 6) - 3;
            var point = new Point(x, y);
            linePoints.Add(point);
            areaPoints.Add(point);
        }

        // 面积（先回到左下角，再沿折线画，最后闭合到右下角）
        var figure = new PathFigure
        {
            StartPoint = new Point(0, height),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new PolyLineSegment { Points = areaPoints });
        figure.Segments.Add(new LineSegment { Point = new Point(width, height) });

        var area = new Path
        {
            Fill = fillBrush,
            Data = new PathGeometry { Figures = { figure } },
        };
        _canvas.Children.Add(area);

        _canvas.Children.Add(new Polyline
        {
            Points = linePoints,
            Stroke = lineBrush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        });

        // 最后一个点的强调
        var last = linePoints[linePoints.Count - 1];
        _canvas.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = lineBrush,
            Margin = new Thickness(last.X - 3, last.Y - 3, 0, 0),
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
            // 资源没加载完就退回硬编码颜色
        }

        return new SolidColorBrush(fallback);
    }
}
