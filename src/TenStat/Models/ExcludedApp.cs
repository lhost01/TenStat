using System;

namespace TenStat.Models;

/// <summary>排除应用（ExcludedApp 表）：前台是该进程时不统计。</summary>
public sealed class ExcludedApp
{
    /// <summary>进程名，不带 .exe，例如 chrome。匹配时忽略大小写。</summary>
    public string ProcessName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public ExcludedApp() { }

    public ExcludedApp(string processName, string displayName = "", bool enabled = true)
    {
        ProcessName = processName ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? ProcessName : displayName;
        Enabled = enabled;
    }
}
