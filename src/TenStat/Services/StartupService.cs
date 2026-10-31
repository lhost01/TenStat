using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace TenStat.Services;

/// <summary>开机自启（HKCU Run，不需要管理员），以及程序自身的路径信息。</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TenStat";

    public static string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path!;
            var asm = Assembly.GetEntryAssembly()?.Location;
            return asm ?? string.Empty;
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            AppLog.Error("读取开机自启失败", ex);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return false;

            if (enabled)
            {
                var exe = ExecutablePath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, "\"" + exe + "\" --tray");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("设置开机自启失败", ex);
            return false;
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("打开目录失败: " + path, ex);
        }
    }

    public static void RevealInExplorer(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + filePath + "\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("定位文件失败: " + filePath, ex);
        }
    }
}
