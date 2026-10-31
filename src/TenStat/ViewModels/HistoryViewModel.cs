using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenStat.Helpers;
using TenStat.Models;
using TenStat.Services;

namespace TenStat.ViewModels;

/// <summary>历史记录页：只读，按日期看。</summary>
public sealed partial class HistoryViewModel : PageViewModelBase
{
    private const int DefaultDays = 14;

    public ObservableCollection<HistoryRowItem> DailyRows { get; } = new();
    public ObservableCollection<KeyRowItem> TopKeys { get; } = new();

    [ObservableProperty]
    private DateTimeOffset? selectedDate = DateTimeOffset.Now;

    [ObservableProperty]
    private int rangeDays = DefaultDays;

    [ObservableProperty]
    private string rangeText = "近 14 天";

    [ObservableProperty]
    private string selectedDateText = "今天";

    [ObservableProperty]
    private string leftText = "0";

    [ObservableProperty]
    private string rightText = "0";

    [ObservableProperty]
    private string pixelsText = "0 px";

    [ObservableProperty]
    private string metersText = "0 米";

    [ObservableProperty]
    private string keysText = "0";

    [ObservableProperty]
    private string activeText = "0 分钟";

    [ObservableProperty]
    private string topKeyText = "—";

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private IReadOnlyList<double> trendValues = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<string> trendLabels = Array.Empty<string>();

    public HistoryViewModel() : base(2000) { }

    partial void OnSelectedDateChanged(DateTimeOffset? value)
    {
        _ = SafeRefreshAsync();
    }

    partial void OnRangeDaysChanged(int value)
    {
        RangeText = $"近 {value} 天";
        _ = SafeRefreshAsync();
    }

    public override async Task RefreshAsync()
    {
        var date = DateOnly.FromDateTime((SelectedDate ?? DateTimeOffset.Now).LocalDateTime);
        SelectedDateText = Formatters.DateDisplay(date) + (date == DateOnly.FromDateTime(DateTime.Now) ? "（今天）" : string.Empty);

        var recent = await Task.Run(() => AppServices.Storage.GetRecentDaily(RangeDays));
        var selected = await Task.Run(() => AppServices.Storage.GetDaily(date));
        var keys = await Task.Run(() => AppServices.Storage.GetKeyStats(date));

        var topKey = keys.FirstOrDefault();
        TopKeyText = topKey?.KeyName ?? "—";

        LeftText = Formatters.Number(selected.LeftClicks);
        RightText = Formatters.Number(selected.RightClicks);
        KeysText = Formatters.Number(selected.KeyPresses);
        PixelsText = Formatters.CompactNumber(selected.MovePixels) + " px";
        MetersText = DpiHelper.FormatMeters(DpiHelper.PixelsToMeters(selected.MovePixels, AppServices.DisplayScale, AppServices.Settings.DpiCalibration));
        ActiveText = Formatters.Duration(selected.ActiveSeconds);

        KeyRowBuilder.Sync(TopKeys, KeyRowBuilder.Build(keys, selected.KeyPresses, 5));
        IsEmpty = !selected.HasData;

        DailyRows.Clear();
        foreach (var stat in recent.OrderByDescending(s => s.Date))
        {
            var dayKeys = await Task.Run(() => AppServices.Storage.GetKeyStats(stat.Date));
            DailyRows.Add(HistoryRowItem.From(stat, dayKeys.FirstOrDefault()?.KeyName ?? "—"));
        }

        // 趋势：按日期从旧到新
        var ordered = recent.OrderBy(s => s.Date).ToList();
        TrendValues = ordered.Select(s => (double)s.TotalClicks).ToList();
        TrendLabels = ordered.Select(s => Formatters.ShortDate(s.Date)).ToList();
    }

    [RelayCommand]
    private void UseLast7Days() => RangeDays = 7;

    [RelayCommand]
    private void UseLast30Days() => RangeDays = 30;

    [RelayCommand]
    private void JumpToToday() => SelectedDate = DateTimeOffset.Now;

    [RelayCommand]
    private void SelectRow(HistoryRowItem? row)
    {
        if (row is null) return;
        SelectedDate = new DateTimeOffset(row.Date.ToDateTime(new TimeOnly(12, 0)));
    }
}
