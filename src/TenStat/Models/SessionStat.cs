using System;

namespace TenStat.Models;

/// <summary>一次运行会话（SessionStat 表）。“本次会话”口径的数据源。</summary>
public sealed class SessionStat
{
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? EndedAt { get; set; }
    public long LeftClicks { get; set; }
    public long RightClicks { get; set; }
    public double MovePixels { get; set; }
    public long KeyPresses { get; set; }
    public long ActiveSeconds { get; set; }

    public long TotalClicks => LeftClicks + RightClicks;
}
