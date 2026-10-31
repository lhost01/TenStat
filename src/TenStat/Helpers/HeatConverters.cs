using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TenStat.Helpers;

/// <summary>
/// 热力图强度 0~1 → 画刷。0 表示没按过（完全透明，等于不显示），1 表示最高频。
/// 用 Accent 色的不同透明度做深浅，自动跟随深浅色主题。
/// </summary>
public sealed partial class IntensityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var intensity = value is double d ? Math.Clamp(d, 0, 1) : 0;
        if (intensity <= 0.0001)
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        var accent = Colors.DodgerBlue;
        try
        {
            if (Application.Current?.Resources is { } resources
                && resources.TryGetValue("AccentFillColorDefaultBrush", out var resource)
                && resource is SolidColorBrush solid)
            {
                accent = solid.Color;
            }
        }
        catch
        {
            // 资源还没加载完，用兜底色
        }

        return new SolidColorBrush(Color.FromArgb((byte)(40 + 200 * intensity), accent.R, accent.G, accent.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
