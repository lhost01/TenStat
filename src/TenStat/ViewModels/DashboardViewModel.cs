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

/// <summary>仪表盘：一屏看清当前口径的整体活动。</summary>
public sealed partial class DashboardViewModel : PageViewModelBase
{
    public ObservableCollection<StatCardItem> Cards { get; } = new();
    public ObservableCollection<KeyRowItem> TopKeys { get; } = new();

    [ObservableProperty]
    private int scopeIndex; // 0=今日 1=本次会话 2=总计

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private string emptyHint = "今天还没有记录，开始使用后自动统计";

    [ObservableProperty]
    private string scopeCaption = "今日";

    [ObservableProperty]
    private IReadOnlyList<double> clickTrend = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> keyTrend = Array.Empty<double>();

    [ObservableProperty]
    private string trendCaption = "最近 1 小时鼠标点击";

    [ObservableProperty]
    private bool showPrivacyTip;

    [ObservableProperty]
    private string privacyTipText = "本程序只统计点击次数、移动距离和按键次数，不记录任何输入内容，不截屏，不联网。";

    [ObservableProperty]
    private string activeDurationText = "0 分钟";

    public StatScope Scope => ScopeIndex switch
    {
        1 => StatScope.Session,
        2 => StatScope.Total,
        _ => StatScope.Today,
    };

    public DashboardViewModel() : base(600)
    {
        Cards.Add(new StatCardItem("左键点击", "\uE962"));
        Cards.Add(new StatCardItem("右键点击", "\uE8A7"));
        Cards.Add(new StatCardItem("鼠标移动", "\uE7C2"));
        Cards.Add(new StatCardItem("键盘按键", "\uE765"));
        Cards.Add(new StatCardItem("按键种类", "\uE8D2"));
        Cards.Add(new StatCardItem("鼠标总点击", "\uE8B0"));
        Cards.Add(new StatCardItem("APM", "\uE9D9"));
        Cards.Add(new StatCardItem("KPM", "\uE9D2"));

        ShowPrivacyTip = AppServices.IsInitialized && !AppServices.Settings.PrivacyAccepted;
    }

    partial void OnScopeIndexChanged(int value)
    {
        ScopeCaption = Scope switch
        {
            StatScope.Session => "本次会话",
            StatScope.Total => "历史总计",
            _ => "今日",
        };
        EmptyHint = Scope switch
        {
            StatScope.Session => "本次会话还没有记录",
            StatScope.Total => "还没有任何历史记录",
            _ => "今天还没有记录，开始使用后自动统计",
        };
        _ = SafeRefreshAsync();
    }

    public override async Task RefreshAsync()
    {
        var stats = AppServices.Stats;
        var scope = Scope;

        if (scope == StatScope.Total) await stats.RefreshTotalKeysAsync();

        var snapshot = stats.GetSnapshot(scope);
        var rows = stats.GetKeyRows(scope);
        var minutes = Math.Max(snapshot.ActiveSeconds, 60) / 60.0;
        var settings = AppServices.Settings;
        var meters = DpiHelper.PixelsToMeters(snapshot.MovePixels, AppServices.DisplayScale, settings.DpiCalibration);

        var leftLabel = scope switch
        {
            StatScope.Session => "本次会话左键",
            StatScope.Total => "历史左键总数",
            _ => "今日左键次数",
        };
        var rightLabel = scope switch
        {
            StatScope.Session => "本次会话右键",
            StatScope.Total => "历史右键总数",
            _ => "今日右键次数",
        };

        Cards[0].Update(Formatters.Number(snapshot.LeftClicks), leftLabel);
        Cards[1].Update(Formatters.Number(snapshot.RightClicks), rightLabel);
        Cards[2].Update(DpiHelper.FormatMeters(meters), Formatters.CompactNumber(snapshot.MovePixels) + " px（需 DPI 校准）");
        Cards[3].Update(Formatters.Number(snapshot.KeyPresses), "键盘按下总次数");
        Cards[4].Update(Formatters.Number(snapshot.KeyKinds), "实际按过的键");
        Cards[5].Update(Formatters.Number(snapshot.TotalClicks), "左键 + 右键");
        Cards[6].Update(Formatters.Rate(snapshot.TotalClicks / minutes), "次 / 分钟");
        Cards[7].Update(Formatters.Rate(snapshot.KeyPresses / minutes), "次 / 分钟");

        ActiveDurationText = Formatters.Duration(snapshot.ActiveSeconds);
        IsEmpty = !snapshot.HasData;

        var trend = stats.GetRecentTrend();
        ClickTrend = trend.Select(p => (double)p.Clicks).ToList();
        KeyTrend = trend.Select(p => (double)p.Keys).ToList();
        TrendCaption = scope == StatScope.Today ? "最近 1 小时鼠标点击" : "最近 1 小时鼠标点击（实时）";

        KeyRowBuilder.Sync(TopKeys, KeyRowBuilder.Build(rows, snapshot.KeyPresses, 10));
        ShowPrivacyTip = false;
    }

    [RelayCommand]
    private void PauseFive() => AppServices.Stats.PauseFor(TimeSpan.FromMinutes(5));

    [RelayCommand]
    private void PauseThirty() => AppServices.Stats.PauseFor(TimeSpan.FromMinutes(30));

    [RelayCommand]
    private void TogglePause() => AppServices.Stats.TogglePause();

    [RelayCommand]
    private void DismissPrivacyTip()
    {
        ShowPrivacyTip = false;
        if (AppServices.IsInitialized) AppServices.Settings.PrivacyAccepted = true;
    }

    [RelayCommand]
    private async Task ExportTodayAsync()
    {
        try
        {
            await AppServices.Stats.FlushAsync();
            var today = AppServices.Stats.Today;
            await Task.Run(() => AppServices.Export.ExportCsv(today, today));
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = "导出失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenSettings() => ShellNavigation.Request("settings");
}

/// <summary>给页面 VM 用的导航通道（MainWindow 订阅）。</summary>
public static class ShellNavigation
{
    public static event EventHandler<string>? Requested;
    public static void Request(string tag) => Requested?.Invoke(null, tag);
}
