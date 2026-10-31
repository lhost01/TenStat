using System;
using TenStat.Helpers;
using TenStat.Models;

namespace TenStat.ViewModels;

/// <summary>历史页表格里的一行。</summary>
public sealed class HistoryRowItem
{
    public DateOnly Date { get; init; }
    public string DateText => Formatters.DateDisplay(Date);
    public string WeekdayText { get; init; } = string.Empty;
    public string LeftText { get; init; } = "0";
    public string RightText { get; init; } = "0";
    public string PixelsText { get; init; } = "0 px";
    public string KeysText { get; init; } = "0";
    public string TopKeyText { get; init; } = "—";

    public static HistoryRowItem From(DailyStat stat, string topKey)
    {
        var weekday = stat.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日",
        };

        return new HistoryRowItem
        {
            Date = stat.Date,
            WeekdayText = weekday,
            LeftText = Formatters.Number(stat.LeftClicks),
            RightText = Formatters.Number(stat.RightClicks),
            PixelsText = Formatters.CompactNumber(stat.MovePixels) + " px",
            KeysText = Formatters.Number(stat.KeyPresses),
            TopKeyText = topKey,
        };
    }
}
