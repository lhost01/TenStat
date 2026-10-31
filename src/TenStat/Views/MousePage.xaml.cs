using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenStat.Helpers;
using TenStat.Services;
using TenStat.ViewModels;

namespace TenStat.Views;

public sealed partial class MousePage : Page
{
    public MouseViewModel ViewModel { get; } = new();

    public MousePage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Attach(DispatcherQueue);
        Unloaded += (_, _) => ViewModel.Detach();
    }

    private async void OnResetMouseClick(object sender, RoutedEventArgs e)
    {
        var confirmed = await DialogHelper.ConfirmAsync(
            XamlRoot,
            "重置今日鼠标统计？",
            "会清空今天的左键、右键和移动距离，键盘统计与历史记录不受影响。此操作不可撤销。",
            "重置",
            "取消");

        if (!confirmed) return;

        await AppServices.Stats.ResetTodayMouseAsync();
        await ViewModel.SafeRefreshAsync();
    }
}
