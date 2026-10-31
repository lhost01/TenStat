using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using TenStat.Helpers;
using TenStat.Models;
using TenStat.Services;

namespace TenStat.ViewModels;

/// <summary>热力图上的一个键。</summary>
public sealed class HeatKeyItem
{
    public int VirtualKeyCode { get; init; }
    public string Label { get; init; } = string.Empty;
    public long Count { get; init; }
    /// <summary>0 = 没按过（不显示），1 = 最高频。</summary>
    public double Intensity { get; init; }
    public double WidthFactor { get; init; } = 1;
    public double WidthPx => Math.Round(40 * WidthFactor);
    public string Tooltip => Count > 0 ? $"{Label}：{Count:N0} 次" : Label;
}

/// <summary>热力图的一行键。</summary>
public sealed class HeatRow
{
    public IReadOnlyList<HeatKeyItem> Keys { get; init; } = Array.Empty<HeatKeyItem>();
}

/// <summary>键盘统计页：排行 + 热力图，只显示按过的键。</summary>
public sealed partial class KeyboardViewModel : PageViewModelBase
{
    private static readonly string[] SortOptions = { "次数降序", "按键名" };

    public ObservableCollection<KeyRowItem> Rows { get; } = new();
    public ObservableCollection<HeatRow> HeatRows { get; } = new();
    public IReadOnlyList<string> Sorts => SortOptions;

    [ObservableProperty]
    private int scopeIndex;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private int sortIndex;

    [ObservableProperty]
    private bool distinguishModifiers = true;

    [ObservableProperty]
    private bool heatmapMode;

    [ObservableProperty]
    private int viewIndex; // 0 = 列表，1 = 热力图

    [ObservableProperty]
    private bool showHeatmap;

    [ObservableProperty]
    private string totalKindsText = "共使用 0 个不同按键";

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private string scopeCaption = "今日";

    [ObservableProperty]
    private string toggleViewText = "热力图";

    public StatScope Scope => ScopeIndex switch
    {
        1 => StatScope.Session,
        2 => StatScope.Total,
        _ => StatScope.Today,
    };

    public KeyboardViewModel() : base(800) { }

    partial void OnScopeIndexChanged(int value) => _ = SafeRefreshAsync();

    partial void OnSearchTextChanged(string value) => _ = SafeRefreshAsync();

    partial void OnSortIndexChanged(int value) => _ = SafeRefreshAsync();

    partial void OnDistinguishModifiersChanged(bool value) => _ = SafeRefreshAsync();

    partial void OnHeatmapModeChanged(bool value)
    {
        ShowHeatmap = value;
        ToggleViewText = value ? "列表" : "热力图";
        _ = SafeRefreshAsync();
    }

    partial void OnViewIndexChanged(int value)
    {
        HeatmapMode = value == 1;
    }

    public override async Task RefreshAsync()
    {
        var stats = AppServices.Stats;
        var scope = Scope;
        if (scope == StatScope.Total) await stats.RefreshTotalKeysAsync();

        var snapshot = stats.GetSnapshot(scope);
        List<KeyStat> all = stats.GetKeyRows(scope).ToList();

        // 「区分修饰键」关掉时，LShift/RShift 等合并成 Shift/Ctrl/Alt/Win
        if (!DistinguishModifiers)
        {
            all = all
                .GroupBy(k => NormalizeModifier(k.VirtualKeyCode))
                .Select(g => new KeyStat
                {
                    Date = g.First().Date,
                    VirtualKeyCode = g.Key,
                    KeyName = KeyNameMapper.GetName(g.Key),
                    Count = g.Sum(x => x.Count),
                    LastPressedAt = g.Max(x => x.LastPressedAt),
                })
                .ToList();
        }

        // 过滤：只保留 Count > 0；搜索框按名字过滤
        IEnumerable<KeyStat> filtered = all.Where(k => k.Count > 0);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            filtered = filtered.Where(k => k.KeyName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filtered = SortIndex == 1
            ? filtered.OrderBy(k => k.KeyName, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderByDescending(k => k.Count);

        var rows = filtered.ToList();
        KeyRowBuilder.Sync(Rows, KeyRowBuilder.Build(rows, snapshot.KeyPresses));

        TotalKindsText = $"共使用 {rows.Count} 个不同按键";
        IsEmpty = rows.Count == 0;
        ScopeCaption = scope == StatScope.Today ? "今日" : scope == StatScope.Session ? "本次会话" : "历史总计";

        if (ShowHeatmap)
        {
            BuildHeatmap(rows);
        }
    }

    private static int NormalizeModifier(int vk) => vk switch
    {
        0xA0 or 0xA1 => 0x10, // LShift / RShift → Shift
        0xA2 or 0xA3 => 0x11, // LCtrl / RCtrl  → Ctrl
        0xA4 or 0xA5 => 0x12, // LAlt / RAlt   → Alt
        0x5C => 0x5B,         // Win(右)       → Win
        _ => vk,
    };

    private void BuildHeatmap(List<KeyStat> rows)
    {
        var map = rows.ToDictionary(r => r.VirtualKeyCode, r => r.Count);
        var max = rows.Count == 0 ? 0 : rows.Max(r => r.Count);

        HeatRows.Clear();
        foreach (var layoutRow in KeyboardLayout.Rows)
        {
            var items = new List<HeatKeyItem>();
            foreach (var key in layoutRow)
            {
                map.TryGetValue(key.Vk, out var count);
                items.Add(new HeatKeyItem
                {
                    VirtualKeyCode = key.Vk,
                    Label = key.Label,
                    Count = count,
                    Intensity = max <= 0 ? 0 : (double)count / max,
                    WidthFactor = key.Width,
                });
            }
            HeatRows.Add(new HeatRow { Keys = items });
        }
    }
}
