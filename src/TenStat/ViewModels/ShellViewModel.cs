using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenStat.Helpers;
using TenStat.Services;

namespace TenStat.ViewModels;

/// <summary>主窗口外壳：状态徽章、暂停/恢复、倒计时、导航请求。</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool isExcluded;

    [ObservableProperty]
    private string statusText = "记录中";

    [ObservableProperty]
    private string statusDetail = "";

    [ObservableProperty]
    private string pauseCountdown = "";

    [ObservableProperty]
    private string todayText = "";

    [ObservableProperty]
    private string trayTooltip = "TenStat — 记录中";

    /// <summary>页面导航请求（Settings / Dashboard ...）。</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>导出完成 / 出错时的提示，由 MainWindow 的 InfoBar 承接。</summary>
    public event EventHandler<(string Message, bool IsError)>? NotificationRaised;

    public ShellViewModel()
    {
        TodayText = Formatters.DateDisplay(DateOnly.FromDateTime(DateTime.Now));
        if (AppServices.IsInitialized)
        {
            AppServices.Stats.Updated += (_, _) => RefreshCountdown();
            AppServices.Stats.StateChanged += (_, _) => Refresh();
        }
    }

    /// <summary>状态刷新（暂停、排除、倒计时）。</summary>
    public void Refresh()
    {
        if (!AppServices.IsInitialized) return;

        var stats = AppServices.Stats;
        IsPaused = stats.IsPaused;
        IsExcluded = stats.IsExcluded;

        if (stats.IsPaused)
        {
            StatusText = "已暂停";
            StatusDetail = stats.PauseRemaining is { } left
                ? $"剩余 {FormatRemaining(left)}"
                : "手动暂停，恢复后继续累计";
        }
        else if (stats.IsExcluded)
        {
            StatusText = "已暂停";
            StatusDetail = "前台应用在排除列表：" + (stats.ExcludedProcess ?? "未知进程");
        }
        else
        {
            StatusText = "记录中";
            StatusDetail = stats.IsSuppressedBySelf
                ? "前台是本程序，这些操作不计入统计"
                : "全局统计，不含输入内容";
        }

        PauseCountdown = stats.PauseRemaining is { } remaining ? FormatRemaining(remaining) : "";
        TrayTooltip = "TenStat — " + (stats.IsCounting ? "记录中" : "已暂停");
        TrayTooltip += $"\n今日：左键 {Formatters.Number(AppServices.Stats.GetSnapshot(Models.StatScope.Today).LeftClicks)} 次";
    }

    private void RefreshCountdown()
    {
        if (!IsPaused) return;
        PauseCountdown = AppServices.Stats.PauseRemaining is { } remaining ? FormatRemaining(remaining) : "";
        StatusDetail = string.IsNullOrEmpty(PauseCountdown)
            ? "手动暂停，恢复后继续累计"
            : "剩余 " + PauseCountdown;
    }

    private static string FormatRemaining(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return "0:00";
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    [RelayCommand]
    private void TogglePause()
    {
        AppServices.Stats.TogglePause();
        AppServices.Keyboard.CountKeyRepeat = AppServices.Settings.CountKeyRepeat;
        Refresh();
        NotificationRaised?.Invoke(this, (IsPaused ? "已暂停统计" : "已恢复统计", false));
    }

    [RelayCommand]
    private void PauseFiveMinutes() => PauseFor(TimeSpan.FromMinutes(5));

    [RelayCommand]
    private void PauseThirtyMinutes() => PauseFor(TimeSpan.FromMinutes(30));

    private void PauseFor(TimeSpan duration)
    {
        AppServices.Stats.PauseFor(duration);
        Refresh();
        NotificationRaised?.Invoke(this, ($"已暂停 {duration.TotalMinutes:0} 分钟，倒计时结束后自动恢复", false));
    }

    [RelayCommand]
    private void Navigate(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        NavigationRequested?.Invoke(this, tag);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportTodayAsync()
    {
        try
        {
            await AppServices.Stats.FlushAsync();
            var today = AppServices.Stats.Today;
            var path = await System.Threading.Tasks.Task.Run(() => AppServices.Export.ExportCsv(today, today));
            NotificationRaised?.Invoke(this, ("已导出今日数据：" + path, false));
        }
        catch (Exception ex)
        {
            AppLog.Error("导出今日数据失败", ex);
            NotificationRaised?.Invoke(this, ("导出失败：" + ex.Message, true));
        }
    }
}
