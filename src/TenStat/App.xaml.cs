using System;
using Microsoft.UI.Xaml;

namespace TenStat;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Services.AppLog.Error("未处理异常", e.Exception);
            Services.AppLog.Error("  ↳ " + e.Exception);
            e.Handled = true; // 统计工具不该因为一次 UI 异常就死掉
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 先建数据层（SQLite 打开 + 设置加载），再建窗口
        Services.AppServices.Initialize();

        Services.AppLog.Info("应用启动，版本 " + typeof(App).Assembly.GetName().Version);

        var window = new MainWindow();
        Services.AppServices.MainWindow = window;
        window.Activate();
    }
}
