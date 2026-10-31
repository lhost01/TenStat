using System;

namespace TenStat.Models;

/// <summary>某天某个虚拟键的按键次数（KeyStat 表）。只存“真按过”的键。</summary>
public sealed class KeyStat
{
    public DateOnly Date { get; set; }
    public int VirtualKeyCode { get; set; }
    public string KeyName { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTimeOffset? LastPressedAt { get; set; }
}
