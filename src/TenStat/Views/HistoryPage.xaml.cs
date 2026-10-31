using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenStat.Helpers;
using TenStat.Services;
using TenStat.ViewModels;

namespace TenStat.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = new();

    public HistoryPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Attach(DispatcherQueue);
        Unloaded += (_, _) => ViewModel.Detach();
    }

    private async void OnExportSelectedDate(object sender, RoutedEventArgs e)
    {
        try
        {
            var date = DateOnly.FromDateTime((ViewModel.SelectedDate ?? DateTimeOffset.Now).LocalDateTime);
            var path = await Task.Run(() => AppServices.Export.ExportCsv(date, date));
            await DialogHelper.ShowAsync(XamlRoot, "导出完成", "已导出到：\n" + path);
            StartupService.RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            AppLog.Error("导出所选日期失败", ex);
            await DialogHelper.ShowAsync(XamlRoot, "导出失败", ex.Message);
        }
    }
}
