using System;

namespace TenStat.ViewModels;

/// <summary>键盘排行里的一行 / 热力图上的一个键。</summary>
public sealed class KeyRowItem
{
    public int VirtualKeyCode { get; init; }
    public string KeyName { get; init; } = string.Empty;
    public long Count { get; init; }
    /// <summary>相对最高频次的 0~1 比例，直接绑 ProgressBar.Value（0~100）。</summary>
    public double RatioPercent { get; init; }
    public string PercentText { get; init; } = "0%";
    public string LastPressedText { get; init; } = "—";
    public string CountText => Count.ToString("N0");

    public string Tooltip => $"{KeyName}：{CountText} 次";
}
