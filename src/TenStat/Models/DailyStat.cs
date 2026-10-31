using System;

namespace TenStat.Models;

/// <summary>某一天的汇总（DailyStat 表）。</summary>
public sealed class DailyStat
{
    public DateOnly Date { get; set; }
    public long LeftClicks { get; set; }
    public long RightClicks { get; set; }
    public double MovePixels { get; set; }
    public long KeyPresses { get; set; }
    public long ActiveSeconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public long TotalClicks => LeftClicks + RightClicks;
    public bool HasData => TotalClicks > 0 || MovePixels > 0.5 || KeyPresses > 0;
}
