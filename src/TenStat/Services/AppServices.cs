using System;
using System.Threading.Tasks;
using TenStat.ViewModels;

namespace TenStat.Services;

/// <summary>
/// 极简服务定位器（不引 DI 容器，少一层依赖）：进程级单例，App 启动时 Initialize。
/// </summary>
public static class AppServices
{
    public static StorageService Storage { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static MouseHookService Mouse { get; private set; } = null!;
    public static KeyboardHookService Keyboard { get; private set; } = null!;
    public static ExclusionService Exclusion { get; private set; } = null!;
    public static StatsService Stats { get; private set; } = null!;
    public static ExportService Export { get; private set; } = null!;
    public static Live2DPetService Pet { get; private set; } = null!;
    public static TrayIcon? Tray { get; private set; }
    public static ShellViewModel Shell { get; private set; } = null!;

    public static MainWindow? MainWindow { get; set; }

    /// <summary>主窗口 HWND（拿 DPI、弹 ContentDialog 都要用）。</summary>
    public static IntPtr WindowHandle
    {
        get
        {
            try
            {
                return MainWindow is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }

    /// <summary>当前显示器的缩放系数（1.0 = 100%）。</summary>
    public static double DisplayScale => Helpers.DpiHelper.GetScale(WindowHandle);

    public static bool IsInitialized { get; private set; }

    public static void Initialize()
    {
        if (IsInitialized) return;

        Storage = new StorageService();
        Storage.Initialize();

        Settings = new SettingsService(Storage);
        Mouse = new MouseHookService();
        Keyboard = new KeyboardHookService { CountKeyRepeat = Settings.CountKeyRepeat };
        Exclusion = new ExclusionService(Storage, Settings);
        Stats = new StatsService(Storage, Settings, Mouse, Keyboard, Exclusion);
        Export = new ExportService(Storage);
        Pet = new Live2DPetService(Storage, Settings);
        Shell = new ShellViewModel();

        IsInitialized = true;
    }

    public static async Task InitializeStatsAsync()
    {
        await Stats.InitializeAsync();
        await Stats.RefreshTotalKeysAsync(force: true);
    }

    public static void AttachTray(TrayIcon tray) => Tray = tray;

    public static async Task ShutdownAsync()
    {
        try
        {
            if (IsInitialized)
            {
                await Stats.ShutdownAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("退出时保存数据失败", ex);
        }
        finally
        {
            try { Tray?.Dispose(); } catch { }
            Tray = null;
            try { Stats?.Dispose(); } catch { }
            try { Storage?.Dispose(); } catch { }
        }
    }
}
