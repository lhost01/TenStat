using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenStat.Helpers;
using TenStat.Models;
using TenStat.Services;

namespace TenStat.ViewModels;

/// <summary>鼠标统计页。</summary>
public sealed partial class MouseViewModel : PageViewModelBase
{
    [ObservableProperty]
    private int scopeIndex;

    [ObservableProperty]
    private string leftClicksText = "0";

    [ObservableProperty]
    private string rightClicksText = "0";

    [ObservableProperty]
    private string leftSubtitle = "左键次数";

    [ObservableProperty]
    private string rightSubtitle = "右键次数";

    [ObservableProperty]
    private double leftRatioPercent;

    /// <summary>环形图要 0~1（不是 0~100）。</summary>
    [ObservableProperty]
    private double leftRatio;

    [ObservableProperty]
    private string ratioText = "左 0% / 右 0%";

    [ObservableProperty]
    private string movePixelsText = "0";

    [ObservableProperty]
    private string moveMetersText = "0 米";

    [ObservableProperty]
    private string speedText = "0 px / 分钟";

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private IReadOnlyList<double> clickTrend = Array.Empty<double>();

    [ObservableProperty]
    private string droppedJumpsText = "0";

    [ObservableProperty]
    private string scopeCaption = "今日";

    public StatScope Scope => ScopeIndex switch
    {
        1 => StatScope.Session,
        2 => StatScope.Total,
        _ => StatScope.Today,
    };

    public MouseViewModel() : base(600) { }

    partial void OnScopeIndexChanged(int value) => _ = SafeRefreshAsync();

    public override async Task RefreshAsync()
    {
        var stats = AppServices.Stats;
        var scope = Scope;
        if (scope == StatScope.Total) await stats.RefreshTotalKeysAsync();

        var snapshot = stats.GetSnapshot(scope);
        var settings = AppServices.Settings;
        var minutes = Math.Max(snapshot.ActiveSeconds, 60) / 60.0;
        var meters = DpiHelper.PixelsToMeters(snapshot.MovePixels, AppServices.DisplayScale, settings.DpiCalibration);
        var clicks = snapshot.TotalClicks;

        LeftClicksText = Formatters.Number(snapshot.LeftClicks);
        RightClicksText = Formatters.Number(snapshot.RightClicks);
        LeftSubtitle = scope == StatScope.Today ? "今日左键次数" : scope == StatScope.Session ? "本次会话左键" : "历史左键总数";
        RightSubtitle = scope == StatScope.Today ? "今日右键次数" : scope == StatScope.Session ? "本次会话右键" : "历史右键总数";

        var ratio = clicks <= 0 ? 0 : snapshot.LeftClicks * 100.0 / clicks;
        LeftRatioPercent = ratio;
        LeftRatio = ratio / 100.0;
        RatioText = $"左 {ratio:0.#}% / 右 {100 - ratio:0.#}%";

        // 米是主显示（用户关心"走了多远"），像素退到次要位置
        MoveMetersText = DpiHelper.FormatMeters(meters);
        MovePixelsText = Formatters.CompactNumber(snapshot.MovePixels) + " px 原始像素（需 DPI 校准）";
        SpeedText = $"{Formatters.Number(snapshot.MovePixels / minutes)} px / 分钟";

        DroppedJumpsText = "已过滤异常跳变：" + Formatters.Number(stats.DroppedJumps) + " 次";
        IsEmpty = clicks == 0 && snapshot.MovePixels < 1;
        ScopeCaption = scope == StatScope.Today ? "今日" : scope == StatScope.Session ? "本次会话" : "历史总计";

        ClickTrend = stats.GetRecentTrend().Select(p => (double)p.Clicks).ToList();
    }

    [RelayCommand]
    private void TogglePause() => AppServices.Stats.TogglePause();
}
