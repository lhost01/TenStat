using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TenStat.Helpers;

namespace TenStat.Services;

/// <summary>
/// 排除应用：前台窗口命中排除列表时暂停统计。
/// 只看进程名（例如 chrome），不看标题、不读内容。
/// </summary>
public sealed class ExclusionService
{
    private readonly StorageService _storage;
    private readonly SettingsService _settings;

    private HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public ExclusionService(StorageService storage, SettingsService settings)
    {
        _storage = storage;
        _settings = settings;
        Reload();
    }

    public bool Enabled
    {
        get => _settings.ExclusionEnabled;
        set
        {
            _settings.ExclusionEnabled = value;
            Reload();
        }
    }

    public void Reload()
    {
        try
        {
            var rows = _storage.LoadExcludedApps();
            _names = new HashSet<string>(
                rows.Where(r => r.Enabled).Select(r => Normalize(r.ProcessName)).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLog.Error("加载排除列表失败", ex);
            _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>前台是否处于排除状态（只算用户配置的排除列表）。</summary>
    public bool IsForegroundExcluded(out string? processName)
    {
        processName = null;
        if (!_settings.ExclusionEnabled) return false;

        var name = GetForegroundProcessName();
        if (string.IsNullOrEmpty(name)) return false;

        if (_names.Contains(name))
        {
            processName = name;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 保留接口但恒为 false：按需求，**前台是本程序时也照常统计**，
    /// 点自己的界面、按自己的快捷键都算数。
    /// </summary>
    public bool IsForegroundAlwaysExcluded(out string? processName)
    {
        processName = null;
        return false;
    }

    /// <summary>当前前台窗口的进程名（不含 .exe）。</summary>
    public static string? GetForegroundProcessName()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            using var process = Process.GetProcessById((int)pid);
            return Normalize(process.ProcessName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>当前前台窗口的进程名 + 显示名，设置页“添加当前前台应用”用。</summary>
    public static (string ProcessName, string DisplayName) GetForegroundProcessInfo()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return (string.Empty, string.Empty);
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return (string.Empty, string.Empty);
            using var process = Process.GetProcessById((int)pid);
            var name = Normalize(process.ProcessName);
            var title = process.MainWindowTitle;
            return (name, string.IsNullOrWhiteSpace(title) ? process.ProcessName : title);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string Normalize(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        return name;
    }
}
