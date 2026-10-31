using System;
using System.Collections.Generic;
using System.Linq;
using TenStat.Helpers;
using TenStat.Models;

namespace TenStat.ViewModels;

/// <summary>把 KeyStat 列表变成 UI 行（含比例、最后一次按下时间）。</summary>
public static class KeyRowBuilder
{
    public static List<KeyRowItem> Build(IReadOnlyList<KeyStat> rows, long totalPresses, int max = int.MaxValue)
    {
        if (rows.Count == 0) return new List<KeyRowItem>();

        var maxCount = rows.Max(r => r.Count);
        var shown = rows.Take(max);

        return shown.Select(r => new KeyRowItem
        {
            VirtualKeyCode = r.VirtualKeyCode,
            KeyName = r.KeyName,
            Count = r.Count,
            RatioPercent = maxCount <= 0 ? 0 : r.Count * 100.0 / maxCount,
            PercentText = totalPresses <= 0 ? "0%" : (r.Count * 100.0 / totalPresses).ToString("0.0") + "%",
            LastPressedText = Formatters.Time(r.LastPressedAt),
        }).ToList();
    }

    /// <summary>内容没变就不动集合，避免每次刷新都重建视觉树（ListView 会闪）。</summary>
    public static void Sync(System.Collections.ObjectModel.ObservableCollection<KeyRowItem> target, List<KeyRowItem> source)
    {
        if (target.Count == source.Count)
        {
            var same = true;
            for (var i = 0; i < source.Count; i++)
            {
                var a = target[i];
                var b = source[i];
                if (a.KeyName != b.KeyName || a.Count != b.Count || !string.Equals(a.LastPressedText, b.LastPressedText, StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }
            if (same) return;
        }

        target.Clear();
        foreach (var item in source) target.Add(item);
    }
}
