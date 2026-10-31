using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TenStat.Helpers;
using TenStat.Models;

namespace TenStat.Services;

/// <summary>导出 CSV / JSON。导出的只有计数、距离和时间戳，绝不含任何输入内容。</summary>
public sealed class ExportService
{
    private readonly StorageService _storage;

    public ExportService(StorageService storage) => _storage = storage;

    public string ExportCsv(DateOnly from, DateOnly to)
    {
        var daily = _storage.GetDailyRange(from, to);
        var keys = new List<KeyStat>();
        foreach (var day in daily)
        {
            keys.AddRange(_storage.GetKeyStats(day.Date));
        }

        var sb = new StringBuilder();
        sb.AppendLine("# TenStat 导出 — 仅含次数与移动距离，不含任何输入内容");
        sb.AppendLine("# 导出时间: " + Formatters.DateTime(DateTimeOffset.Now));
        sb.AppendLine("# 日期范围: " + Formatters.DateKey(from) + " ~ " + Formatters.DateKey(to));
        sb.AppendLine();
        sb.AppendLine("[DailyStat]");
        sb.AppendLine("Date,LeftClicks,RightClicks,MovePixels,KeyPresses,ActiveSeconds,UpdatedAt");
        foreach (var d in daily.OrderBy(d => d.Date))
        {
            sb.AppendLine(string.Join(',',
                d.Date.ToString(Formatters.DateFormat, CultureInfo.InvariantCulture),
                d.LeftClicks, d.RightClicks,
                d.MovePixels.ToString("0.##", CultureInfo.InvariantCulture),
                d.KeyPresses, d.ActiveSeconds,
                d.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        sb.AppendLine();
        sb.AppendLine("[KeyStat]");
        sb.AppendLine("Date,VirtualKeyCode,KeyName,Count,LastPressedAt");
        foreach (var k in keys.OrderBy(k => k.Date).ThenByDescending(k => k.Count))
        {
            sb.AppendLine(string.Join(',',
                k.Date.ToString(Formatters.DateFormat, CultureInfo.InvariantCulture),
                k.VirtualKeyCode,
                EscapeCsv(k.KeyName),
                k.Count,
                k.LastPressedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty));
        }

        var path = BuildPath("csv");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    public string ExportJson(DateOnly from, DateOnly to)
    {
        var daily = _storage.GetDailyRange(from, to).OrderBy(d => d.Date).ToList();
        var payload = new
        {
            app = "TenStat",
            privacy = "只统计次数与移动距离，不记录任何输入内容",
            exportedAt = Formatters.DateTime(DateTimeOffset.Now),
            from = Formatters.DateKey(from),
            to = Formatters.DateKey(to),
            daily = daily.Select(d => new
            {
                date = Formatters.DateKey(d.Date),
                leftClicks = d.LeftClicks,
                rightClicks = d.RightClicks,
                movePixels = Math.Round(d.MovePixels, 2),
                keyPresses = d.KeyPresses,
                activeSeconds = d.ActiveSeconds,
            }),
            keys = daily.SelectMany(d => _storage.GetKeyStats(d.Date).Select(k => new
            {
                date = Formatters.DateKey(k.Date),
                virtualKeyCode = k.VirtualKeyCode,
                keyName = k.KeyName,
                count = k.Count,
                lastPressedAt = k.LastPressedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            })).OrderByDescending(k => k.count),
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            // 中文不要被转义成 \uXXXX，方便人直接看
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var path = BuildPath("json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private string BuildPath(string extension)
    {
        Directory.CreateDirectory(_storage.ExportDirectory);
        var name = "stats-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "." + extension;
        return Path.Combine(_storage.ExportDirectory, name);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
