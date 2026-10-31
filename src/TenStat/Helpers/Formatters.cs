using System;
using System.Globalization;

namespace TenStat.Helpers;

/// <summary>统一的数字/时间格式化，保证所有页面的数字长得一样。</summary>
public static class Formatters
{
    public const string DateFormat = "yyyy-MM-dd";

    public static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Number(double value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Decimal2(double value) => value.ToString("N2", CultureInfo.CurrentCulture);

    public static string Rate(double value) => value.ToString("0.0", CultureInfo.CurrentCulture);

    public static string DateKey(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string DateDisplay(DateOnly date)
        => date.ToString("yyyy 年 M 月 d 日", CultureInfo.CurrentCulture);

    public static string ShortDate(DateOnly date)
        => date.ToString("M/d", CultureInfo.CurrentCulture);

    public static string Time(DateTimeOffset? time)
        => time is null ? "—" : time.Value.LocalDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public static string DateTime(DateTimeOffset time)
        => time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>秒 → “1 小时 23 分”。</summary>
    public static string Duration(long seconds)
    {
        if (seconds <= 0) return "0 分钟";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} 小时 {ts.Minutes} 分";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes} 分钟";
        return $"{seconds} 秒";
    }

    /// <summary>把像素数压缩成 “1.2 万 / 3.4 亿” 便于卡片展示。</summary>
    public static string CompactNumber(double value)
    {
        var abs = Math.Abs(value);
        if (abs >= 100_000_000) return (value / 100_000_000).ToString("0.##") + " 亿";
        if (abs >= 10_000) return (value / 10_000).ToString("0.##") + " 万";
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }
}
