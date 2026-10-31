using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace TenStat.Helpers;

/// <summary>bool → Visibility，Invert=true 时取反。</summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (Invert) flag = !flag;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>非空字符串 → Visible（空状态/错误态用）。</summary>
public sealed partial class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var has = !string.IsNullOrWhiteSpace(value as string);
        if (Invert) has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>bool → 主题资源里的画刷（按 key 取）。</summary>
public sealed partial class BoolToStatusBrushConverter : IValueConverter
{
    public string TrueBrushKey { get; set; } = "StatusPausedBrush";
    public string FalseBrushKey { get; set; } = "StatusRecordingBrush";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        var key = flag ? TrueBrushKey : FalseBrushKey;
        if (Application.Current?.Resources is { } res && res.TryGetValue(key, out var brush) && brush is Brush b2)
        {
            return b2;
        }
        return new SolidColorBrush(flag ? Microsoft.UI.Colors.IndianRed : Microsoft.UI.Colors.ForestGreen);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>bool → 文本。</summary>
public sealed partial class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "真";
    public string FalseText { get; set; } = "假";

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? TrueText : FalseText;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>bool → Segoe Fluent Icons 字形。</summary>
public sealed partial class BoolToGlyphConverter : IValueConverter
{
    public string TrueGlyph { get; set; } = "\uE769";
    public string FalseGlyph { get; set; } = "\uE768";

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? TrueGlyph : FalseGlyph;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>0~1 → 百分比文本。</summary>
public sealed partial class RatioToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var d = value is double x ? x : 0d;
        return string.Create(CultureInfo.InvariantCulture, $"{d * 100:0.#}%");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
