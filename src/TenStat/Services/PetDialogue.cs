using System;
using System.Collections.Generic;
using TenStat.Helpers;
using TenStat.Models;
using TenStat.Services;

namespace TenStat.Services;

/// <summary>
/// 桌宠的台词生成：只用"今天的统计数字"写话，不碰任何输入内容。
/// 文案按数据分批：空数据 / 鼠标 / 键盘 / 距离 / 深夜或清早 / 纯闲聊兜底。
/// </summary>
public static class PetDialogue
{
    private static readonly Random Rng = new();

    /// <summary>根据今日统计生成一句话。</summary>
    public static string Compose(StatSnapshot today, IReadOnlyList<KeyStat> keyRows, DateTime now)
    {
        var lines = new List<string>();

        if (!today.HasData)
        {
            lines.AddRange(new[]
            {
                "今天还没开始记录呢，动动鼠标让我看看～",
                "我在这儿等着呢，先点我一下？",
                "现在是零哦，一整天都还没动过鼠标。",
            });
            return Pick(lines);
        }

        // 时间段问候
        if (now.Hour >= 0 && now.Hour < 6)
        {
            lines.Add("这么晚还在用电脑，注意休息呀。");
        }
        else if (now.Hour >= 6 && now.Hour < 11)
        {
            lines.Add("早上好，今天也要加油哦。");
        }
        else if (now.Hour >= 22)
        {
            lines.Add("快十一点了，早点睡吧。");
        }

        // 鼠标
        if (today.LeftClicks >= 800)
        {
            lines.Add($"左键已经按了 {today.LeftClicks:N0} 次了，手指还撑得住吗？");
        }
        else if (today.LeftClicks >= 100)
        {
            lines.Add($"今天左键 {today.LeftClicks:N0} 次，右键只有 {today.RightClicks:N0} 次，你是不是不太用右键？");
        }
        else if (today.TotalClicks > 0)
        {
            lines.Add($"今天点了 {today.TotalClicks:N0} 下鼠标，还挺克制的。");
        }

        if (today.RightClicks == 0 && today.LeftClicks > 50)
        {
            lines.Add("一次右键都没用过，全靠左键硬点。");
        }

        // 键盘
        if (today.KeyPresses >= 8000)
        {
            lines.Add($"键盘敲了 {today.KeyPresses:N0} 次……今天话很多嘛。");
        }
        else if (today.KeyPresses > 0)
        {
            lines.Add($"键盘 {today.KeyPresses:N0} 次，用了 {today.KeyKinds} 个不同的键。");
        }

        if (keyRows.Count > 0)
        {
            var top = keyRows[0];
            if (top.Count >= 200)
            {
                lines.Add($"「{top.KeyName}」被你按了 {top.Count:N0} 次，它今天最辛苦。");
            }
            else if (top.Count >= 30)
            {
                lines.Add($"最常按的是「{top.KeyName}」，一共 {top.Count:N0} 次。");
            }
        }

        // 距离
        var meters = DpiHelper.PixelsToMeters(today.MovePixels, AppServices.DisplayScale, AppServices.Settings.DpiCalibration);
        if (meters >= 200)
        {
            lines.Add($"鼠标今天走了 {meters:N0} 米，够绕操场半圈了。");
        }
        else if (meters >= 30)
        {
            lines.Add($"鼠标移动了 {meters:N0} 米，差不多是房间走两圈的距离。");
        }
        else if (meters >= 1)
        {
            lines.Add($"鼠标挪了 {meters:0.0} 米，手还挺稳的。");
        }

        // 活跃时长
        if (today.ActiveSeconds >= 3600)
        {
            lines.Add($"今天在电脑前活跃了 {today.ActiveSeconds / 3600} 个多小时，记得起来走走。");
        }

        if (lines.Count == 0)
        {
            lines.AddRange(new[]
            {
                "今天的数字还很少，多陪我一会儿嘛。",
                "我在看着你的统计呢。",
                "点我一下，我再跟你说点别的。",
            });
        }

        return Pick(lines);
    }

    /// <summary>被点一下时的短句（互动感更强）。</summary>
    public static string ComposeTap(StatSnapshot today, string? topKeyName)
    {
        var lines = new List<string>
        {
            "诶？你点我。",
            "在的在的。",
            "摸摸头也没用哦。",
            "要不要看看今天的统计？",
        };

        if (today.TotalClicks > 0)
        {
            lines.Add($"刚才那一下也算进去了，现在一共 {today.TotalClicks + 1:N0} 次点击。");
        }

        if (!string.IsNullOrEmpty(topKeyName))
        {
            lines.Add($"你今天最爱按的是「{topKeyName}」。");
        }

        return Pick(lines);
    }

    private static string Pick(IReadOnlyList<string> lines)
        => lines.Count == 0 ? "……" : lines[Rng.Next(lines.Count)];
}
