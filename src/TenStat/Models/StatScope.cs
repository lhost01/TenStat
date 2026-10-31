namespace TenStat.Models;

/// <summary>统计口径：只影响展示，不影响后台累计。</summary>
public enum StatScope
{
    Today = 0,
    Session = 1,
    Total = 2
}
