using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenStat.Helpers;
using TenStat.Models;
using TenStat.Services;

namespace TenStat.ViewModels;

/// <summary>设置页。危险操作只提供“执行体”，二次确认放在页面 code-behind 里做。</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private bool _loading = true;

    public ObservableCollection<ExcludedApp> ExcludedApps { get; } = new();

    public string[] ThemeOptions { get; } = { "跟随系统", "浅色", "深色" };
    public string[] DistanceUnitOptions { get; } = { "像素", "米" };

    [ObservableProperty]
    private bool startWithWindows;

    [ObservableProperty]
    private bool minimizeToTray;

    [ObservableProperty]
    private int themeIndex;

    [ObservableProperty]
    private bool countKeyRepeat;

    [ObservableProperty]
    private int distanceUnitIndex = 1;

    [ObservableProperty]
    private double dpiCalibration = 1.0;

    [ObservableProperty]
    private double autoSaveSeconds = 15;

    [ObservableProperty]
    private string pauseHotkey = "Ctrl+Alt+P";

    [ObservableProperty]
    private bool isRecordingHotkey;

    [ObservableProperty]
    private bool exclusionEnabled = true;

    [ObservableProperty]
    private bool petEnabled = true;

    [ObservableProperty]
    private bool petStayInsideApp = true;

    [ObservableProperty]
    private string petModelPath = string.Empty;

    [ObservableProperty]
    private double petScale = 1.0;

    [ObservableProperty]
    private string dataPath = string.Empty;

    [ObservableProperty]
    private string databasePath = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string newExcludedName = string.Empty;

    [ObservableProperty]
    private ExcludedApp? selectedExcludedApp;

    [ObservableProperty]
    private string versionText = "1.0.0";

    public event EventHandler<int>? ThemeChangeRequested;

    public SettingsViewModel()
    {
        Load();
        _loading = false;
    }

    public void Load()
    {
        if (!AppServices.IsInitialized) return;

        var s = AppServices.Settings;
        StartWithWindows = StartupService.IsEnabled();
        MinimizeToTray = s.MinimizeToTray;
        ThemeIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        CountKeyRepeat = s.CountKeyRepeat;
        DistanceUnitIndex = s.DistanceUnit == "Pixels" ? 0 : 1;
        DpiCalibration = s.DpiCalibration;
        AutoSaveSeconds = s.AutoSaveIntervalSeconds;
        PauseHotkey = s.PauseHotkey;
        ExclusionEnabled = s.ExclusionEnabled;
        PetEnabled = s.PetEnabled;
        PetStayInsideApp = s.PetStayInsideApp;
        PetModelPath = s.PetModelDirectory;
        PetScale = s.PetScale;
        DataPath = AppServices.Storage.DataDirectory;
        DatabasePath = AppServices.Storage.DatabasePath;
        VersionText = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        ReloadExcluded();
    }

    public void ReloadExcluded()
    {
        ExcludedApps.Clear();
        foreach (var app in AppServices.Storage.LoadExcludedApps())
        {
            ExcludedApps.Add(app);
        }
    }

    // ---------------------------------------------------------------- 属性联动

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        if (StartupService.SetEnabled(value))
        {
            AppServices.Settings.StartWithWindows = value;
            StatusMessage = value ? "已设置开机自启（启动后最小化到托盘）" : "已关闭开机自启";
        }
        else
        {
            StatusMessage = "设置开机自启失败，请检查注册表权限";
        }
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.MinimizeToTray = value;
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.Theme = value switch { 1 => "Light", 2 => "Dark", _ => "System" };
        ThemeChangeRequested?.Invoke(this, value);
    }

    partial void OnCountKeyRepeatChanged(bool value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.CountKeyRepeat = value;
        AppServices.Keyboard.CountKeyRepeat = value;
    }

    partial void OnDistanceUnitIndexChanged(int value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.DistanceUnit = value == 0 ? "Pixels" : "Meters";
    }

    partial void OnDpiCalibrationChanged(double value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.DpiCalibration = value;
    }

    partial void OnAutoSaveSecondsChanged(double value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.AutoSaveIntervalSeconds = (int)Math.Round(value);
    }

    partial void OnPauseHotkeyChanged(string value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.PauseHotkey = value;
        ApplyHotkey();
    }

    partial void OnExclusionEnabledChanged(bool value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Exclusion.Enabled = value;
    }

    partial void OnPetEnabledChanged(bool value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.PetEnabled = value;
        AppServices.MainWindow?.UpdatePetPanel(value);
        StatusMessage = value ? "桌宠已显示在主窗口右下角" : "桌宠已隐藏（标题栏的小图标可以再打开）";
    }

    partial void OnPetStayInsideAppChanged(bool value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.PetStayInsideApp = value;
        AppServices.MainWindow?.ApplyPetStayInside(value);
        StatusMessage = value
            ? "桌宠只在本程序前台时出现（玩游戏/切到别的窗口会隐藏）"
            : "桌宠常驻显示，可以拖到屏幕任意位置";
    }

    partial void OnPetScaleChanged(double value)
    {
        if (_loading || !AppServices.IsInitialized) return;
        AppServices.Settings.PetScale = value;
        AppServices.MainWindow?.ApplyPetScale(value);
    }

    // ---------------------------------------------------------------- 命令

    [RelayCommand]
    private void ApplyHotkey()
    {
        var tray = AppServices.Tray;
        if (tray is null) return;

        if (!TrayIcon.TryParseHotkey(PauseHotkey, out var modifiers, out var vk))
        {
            StatusMessage = "快捷键格式不对，示例：Ctrl+Alt+P";
            return;
        }

        StatusMessage = tray.RegisterHotkey(modifiers, vk, out var error)
            ? $"暂停快捷键已更新：{KeyNameMapper.DescribeHotkey(modifiers, vk)}"
            : "注册快捷键失败：" + error;
    }

    [RelayCommand]
    private void BeginRecordHotkey()
    {
        IsRecordingHotkey = true;
        StatusMessage = "请直接按下想用的组合键（例如 Ctrl+Alt+P），Esc 取消";
    }

    /// <summary>页面 code-behind 抓到按键后调这里。</summary>
    public void ApplyRecordedHotkey(uint modifiers, uint vk)
    {
        if (!IsRecordingHotkey) return;
        IsRecordingHotkey = false;

        if (modifiers == 0)
        {
            StatusMessage = "请带上至少一个修饰键（Ctrl / Alt / Shift / Win）";
            return;
        }

        PauseHotkey = KeyNameMapper.DescribeHotkey(modifiers, vk);
        ApplyHotkey();
    }

    [RelayCommand]
    private void CancelRecordHotkey()
    {
        IsRecordingHotkey = false;
        StatusMessage = "已取消录制";
    }

    [RelayCommand]
    private void OpenDataFolder() => StartupService.OpenFolder(AppServices.Storage.DataDirectory);

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            await AppServices.Stats.FlushAsync();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var path = await Task.Run(() => AppServices.Export.ExportCsv(today.AddDays(-30), today));
            StatusMessage = "CSV 已导出：" + path;
            StartupService.RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            AppLog.Error("导出 CSV 失败", ex);
            StatusMessage = "导出 CSV 失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        try
        {
            await AppServices.Stats.FlushAsync();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var path = await Task.Run(() => AppServices.Export.ExportJson(today.AddDays(-30), today));
            StatusMessage = "JSON 已导出：" + path;
            StartupService.RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            AppLog.Error("导出 JSON 失败", ex);
            StatusMessage = "导出 JSON 失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void PickPetModelFolder()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, AppServices.WindowHandle);

        _ = PickAsync();

        async Task PickAsync()
        {
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            PetModelPath = folder.Path;
            AppServices.Settings.PetModelDirectory = folder.Path;
            StatusMessage = "模型目录已更新：" + folder.Path;
            AppServices.MainWindow?.ReloadPetPanel();
        }
    }

    [RelayCommand]
    private void PauseFive() => AppServices.Stats.PauseFor(TimeSpan.FromMinutes(5));

    [RelayCommand]
    private void PauseThirty() => AppServices.Stats.PauseFor(TimeSpan.FromMinutes(30));

    [RelayCommand]
    private void Resume() => AppServices.Stats.Resume();

    [RelayCommand]
    private void AddExcluded()
    {
        var name = (NewExcludedName ?? string.Empty).Trim();
        if (name.Length == 0) return;
        SaveExcluded(new ExcludedApp(name, name));
        NewExcludedName = string.Empty;
        ReloadExcluded();
    }

    [RelayCommand]
    private void AddForegroundApp()
    {
        var (processName, displayName) = ExclusionService.GetForegroundProcessInfo();
        if (string.IsNullOrWhiteSpace(processName))
        {
            StatusMessage = "没有取到前台窗口的进程（切到目标窗口后再点一次）";
            return;
        }
        SaveExcluded(new ExcludedApp(processName, displayName));
        ReloadExcluded();
        StatusMessage = $"已排除：{processName}";
    }

    private void SaveExcluded(ExcludedApp app)
    {
        AppServices.Storage.UpsertExcludedApp(app);
        AppServices.Exclusion.Reload();
    }

    [RelayCommand]
    private void RemoveExcluded()
    {
        if (SelectedExcludedApp is null) return;
        AppServices.Storage.DeleteExcludedApp(SelectedExcludedApp.ProcessName);
        AppServices.Exclusion.Reload();
        ReloadExcluded();
        StatusMessage = "已移除：" + SelectedExcludedApp.ProcessName;
        SelectedExcludedApp = null;
    }

    // ---------------------------------------------------------------- 危险操作（由页面负责二次确认）

    public async Task ClearTodayAsync()
    {
        await AppServices.Stats.ResetTodayAsync();
        StatusMessage = "今日数据已清空";
    }

    public async Task ClearAllAsync()
    {
        await AppServices.Stats.ResetAllAsync();
        ReloadExcluded();
        StatusMessage = "全部统计数据已清空（设置与排除列表保留）";
    }

    public void RestoreDefaultSettings()
    {
        AppServices.Settings.RestoreDefaults();
        AppServices.Keyboard.CountKeyRepeat = AppServices.Settings.CountKeyRepeat;
        Load();
        ApplyHotkey();
        StatusMessage = "设置已恢复默认";
    }
}
