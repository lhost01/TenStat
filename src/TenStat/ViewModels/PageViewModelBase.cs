using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using TenStat.Services;

namespace TenStat.ViewModels;

/// <summary>
/// 页面 VM 公共部分：页面可见时按固定节奏刷新，离开就停，避免后台页面空转。
/// 钩子回调不进 UI，UI 只按节奏拿快照——这就是“钩子不卡 UI、UI 不卡钩子”。
/// </summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    private DispatcherQueueTimer? _timer;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    protected PageViewModelBase(int intervalMs = 600)
    {
        IntervalMs = intervalMs;
    }

    protected int IntervalMs { get; }

    /// <summary>状态徽章 / 暂停按钮共用同一个 Shell VM，页面直接绑它。</summary>
    public ShellViewModel Shell => AppServices.Shell;

    public void Attach(DispatcherQueue dispatcher)
    {
        if (_timer is not null) return;

        _timer = dispatcher.CreateTimer();
        if (_timer is null) return;
        _timer.Interval = TimeSpan.FromMilliseconds(IntervalMs);
        _timer.Tick += OnTick;
        _timer.Start();

        _ = SafeRefreshAsync();
    }

    public void Detach()
    {
        if (_timer is null) return;
        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => _ = SafeRefreshAsync();

    public async Task SafeRefreshAsync()
    {
        if (!AppServices.IsInitialized) return;
        try
        {
            HasError = false;
            ErrorText = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(GetType().Name + " 刷新失败", ex);
            AppLog.Error("  ↳ " + ex);
            HasError = true;
            ErrorText = ex.Message;
        }
    }

    /// <summary>页面自己实现的数据刷新（只在 UI 线程跑）。</summary>
    public abstract Task RefreshAsync();
}
