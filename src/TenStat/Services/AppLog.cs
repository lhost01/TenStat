using System;
using System.IO;

namespace TenStat.Services;

/// <summary>
/// 极简本地日志：只写程序自身的运行事件（异常、钩子安装失败等），
/// 不记录任何输入内容。文件：%LocalAppData%\TenStat\app.log
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string LogPath
    {
        get
        {
            if (_path is not null) return _path;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TenStat");
            try { Directory.CreateDirectory(dir); } catch { }
            _path = Path.Combine(dir, "app.log");
            return _path;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : message + " :: " + ex.GetType().Name + ": " + ex.Message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // 日志失败绝不能影响主流程
        }
    }
}
