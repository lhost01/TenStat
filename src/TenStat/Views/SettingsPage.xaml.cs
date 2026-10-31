using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TenStat.Helpers;
using TenStat.Services;
using TenStat.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace TenStat.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();

        // 每次进设置页都对齐一次（桌宠开关可能在标题栏被切过）
        Loaded += (_, _) => ViewModel.Load();

        ViewModel.ThemeChangeRequested += (_, index) => AppServices.MainWindow?.ApplyTheme(index switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        });

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.IsRecordingHotkey) && ViewModel.IsRecordingHotkey)
            {
                RootGrid.Focus(FocusState.Programmatic);
            }
        };
    }

    private void OnSettingsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.IsRecordingHotkey) return;

        e.Handled = true;

        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.CancelRecordHotkeyCommand.Execute(null);
            return;
        }

        // 只按修饰键不算，等主键
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        uint modifiers = 0;
        if (IsDown(VirtualKey.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (IsDown(VirtualKey.Menu)) modifiers |= NativeMethods.MOD_ALT;
        if (IsDown(VirtualKey.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= NativeMethods.MOD_WIN;

        ViewModel.ApplyRecordedHotkey(modifiers, (uint)e.Key);
    }

    private static bool IsDown(VirtualKey key)
    {
        try
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
            return state.HasFlag(CoreVirtualKeyStates.Down);
        }
        catch
        {
            return false;
        }
    }

    private async void OnClearToday(object sender, RoutedEventArgs e)
    {
        var confirmed = await DialogHelper.ConfirmAsync(
            XamlRoot,
            "清空今日数据？",
            "会删除今天所有的鼠标点击、移动距离和按键次数。历史记录不受影响，此操作不可撤销。",
            "清空今日",
            "取消");

        if (!confirmed) return;
        await ViewModel.ClearTodayAsync();
    }

    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        var confirmed = await DialogHelper.ConfirmAsync(
            XamlRoot,
            "清空全部统计数据？",
            "会删除全部日期的鼠标、键盘与会话统计（设置项和排除列表会保留）。此操作不可撤销，建议先导出备份。",
            "全部清空",
            "取消");

        if (!confirmed) return;
        await ViewModel.ClearAllAsync();
    }

    private async void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var confirmed = await DialogHelper.ConfirmAsync(
            XamlRoot,
            "恢复默认设置？",
            "会把主题、统计选项、快捷键、桌宠设置恢复成默认值。统计数据不会被删除。",
            "恢复默认",
            "取消");

        if (!confirmed) return;
        ViewModel.RestoreDefaultSettings();
        AppServices.MainWindow?.ApplyTheme(ElementTheme.Default);
    }
}
