using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TenStat.Controls;
using TenStat.Helpers;
using TenStat.Services;
using TenStat.ViewModels;
using TenStat.Views;
using Windows.Graphics;

namespace TenStat;

/// <summary>
/// 主窗口：NavigationView + Frame，顶部自定义标题栏 + 全局 InfoBar，底部状态徽章与暂停入口。
/// 关闭窗口默认最小化到托盘（设置可关），真正退出走托盘菜单或设置里的“退出”。
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int MinWidth = 900;
    private const int MinHeight = 640;

    private readonly ShellViewModel _shell = AppServices.Shell;
    private AppWindow? _appWindow;
    private bool _reallyExit;
    private bool _resizing;
    private bool _startupCompleted;

    /// <summary>防重入：UpdateShellUi → Shell.Refresh() 会触发 PropertyChanged，再回到 UpdateShellUi 就栈溢出了。</summary>
    private bool _updatingShellUi;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "TenStat";

        ConfigureWindow();
        ApplyBackdrop();
        ApplyTheme(ParseTheme(AppServices.Settings.Theme));

        _shell.NavigationRequested += (_, tag) => NavigateTo(tag);
        _shell.PropertyChanged += (_, _) => UpdateShellUi();
        ShellNavigation.Requested += (_, tag) => NavigateTo(tag);

        Nav.SelectedItem = Nav.MenuItems[0];
        NavigateTo("dashboard");
        UpdateShellUi();

        // Window 本身没有 Loaded，用根元素
        RootGrid.Loaded += OnWindowLoaded;
    }

    // ---------------------------------------------------------------- 窗口外壳

    private void ConfigureWindow()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(id);

            _appWindow.Title = "TenStat";
            _appWindow.Resize(new SizeInt32(1180, 780));

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                try { _appWindow.SetIcon(iconPath); } catch (Exception ex) { AppLog.Warn("设置窗口图标失败：" + ex.Message); }
            }

            // 最小尺寸：WinUI 3 没有原生 MinWidth，这里在尺寸变化时夹回来
            _appWindow.Changed += OnAppWindowChanged;
            _appWindow.Closing += OnAppWindowClosing;
        }
        catch (Exception ex)
        {
            AppLog.Error("配置窗口失败", ex);
        }
    }

    private void ApplyBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            }
            else if (DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Mica 不可用，退回默认背景：" + ex.Message);
        }
    }

    private static ElementTheme ParseTheme(string theme) => theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public void ApplyTheme(ElementTheme theme)
    {
        if (RootGrid is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }

    /// <summary>桌宠大小：缩放浮层窗口，页面自己按窗口尺寸自适应。</summary>
    public void ApplyPetStayInside(bool stayInside) => _petOverlay?.SetStayInside(stayInside);

    public void ApplyPetScale(double scale)
    {
        _petOverlay?.SetScale(scale);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange)
        {
            _petOverlay?.SyncWithOwner(AppServices.WindowHandle);
        }

        if (!args.DidSizeChange || _resizing) return;
        var size = sender.Size;
        if (size.Width >= MinWidth && size.Height >= MinHeight) return;

        _resizing = true;
        try
        {
            sender.Resize(new SizeInt32(Math.Max(MinWidth, size.Width), Math.Max(MinHeight, size.Height)));
        }
        finally
        {
            _resizing = false;
        }
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        AppLog.Info("[关闭窗口] reallyExit=" + _reallyExit + " minimizeToTray=" + AppServices.Settings.MinimizeToTray);

        if (_reallyExit)
        {
            await AppServices.ShutdownAsync();
            return;
        }

        if (!AppServices.Settings.MinimizeToTray) return;

        // 取消关闭 → 藏到托盘，统计继续（桌宠一起收起来）
        args.Cancel = true;
        sender.Hide();
        _petOverlay?.Hide();
        ShowInfo("TenStat 已最小化到托盘，统计仍在继续。", false, "已最小化");
    }

    // ---------------------------------------------------------------- 启动流程

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_startupCompleted) return;
        _startupCompleted = true;
        await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            if (!AppServices.Settings.PrivacyAccepted)
            {
                var accepted = await ShowPrivacyDialogAsync();
                if (accepted)
                {
                    AppServices.Settings.PrivacyAccepted = true;
                }
                else
                {
                    ShowInfo("未同意隐私说明，统计未启动。可在设置页重新查看隐私说明后开启。", false, "统计未启动");
                    UpdateShellUi();
                    return;
                }
            }

            AppServices.Mouse.Failed += (_, message) => ShowInfo(message, true, "鼠标监听失败");
            AppServices.Keyboard.Failed += (_, message) => ShowInfo(message, true, "键盘监听失败");
            AppServices.Stats.ErrorRaised += (_, message) => ShowInfo(message, true, "统计出错");

            var mouseOk = AppServices.Mouse.Start();
            var keyboardOk = AppServices.Keyboard.Start();
            AppServices.Keyboard.CountKeyRepeat = AppServices.Settings.CountKeyRepeat;

            if (!mouseOk && !keyboardOk)
            {
                ShowInfo("全局钩子安装失败：统计无法工作。请检查安全软件是否拦截了低级键盘/鼠标钩子。", true, "监听失败");
            }

            await AppServices.InitializeStatsAsync();
            AppServices.Stats.Start(DispatcherQueue);
            AppServices.Stats.StateChanged += (_, _) => UpdateShellUi();
            AppServices.Stats.Updated += (_, _) => UpdateShellUi();

            InitializeTray();
            UpdateShellUi();

            AppServices.Pet.Failed += (_, message) => ShowInfo(message, InfoBarSeverity.Warning, "桌宠");
            UpdatePetPanel(AppServices.Settings.PetEnabled);

            // --tray：开机自启时直接躲进托盘
            var args = Environment.GetCommandLineArgs();
            if (Array.Exists(args, a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
            {
                _appWindow?.Hide();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("启动流程失败", ex);
            ShowInfo("启动失败：" + ex.Message, true, "启动失败");
        }
    }

    private async Task<bool> ShowPrivacyDialogAsync()
    {
        try
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "这个程序只做三件事：数点击次数、量鼠标移动距离、数每个键被按了多少次。",
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new TextBlock
            {
                Text = "它不会记录你输入了什么内容：不保存文字、不截屏、不记录鼠标轨迹、不联网、不上传任何数据。所有数据只存在本机 %LocalAppData%\\TenStat\\stats.db。",
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new TextBlock
            {
                Text = "统计到的按键名称形如 A、Enter、VK_6A，不包含大小写与字符内容。你随时可以在托盘或设置里暂停统计。",
                TextWrapping = TextWrapping.Wrap,
            });

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "隐私说明",
                Content = content,
                PrimaryButtonText = "同意并开始统计",
                CloseButtonText = "暂不统计",
                DefaultButton = ContentDialogButton.Primary,
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            AppLog.Error("隐私对话框异常", ex);
            return false;
        }
    }

    private void InitializeTray()
    {
        try
        {
            var tray = new TrayIcon();
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (!tray.Initialize(iconPath))
            {
                ShowInfo("托盘图标初始化失败，关窗后将无法从托盘恢复（可在任务管理器结束进程）。", true, "托盘");
                tray.Dispose();
                return;
            }

            tray.ShowRequested += (_, _) => ShowFromTray();
            tray.TogglePauseRequested += (_, _) =>
            {
                AppServices.Stats.TogglePause();
                UpdateShellUi();
            };
            tray.ExportRequested += async (_, _) =>
            {
                try
                {
                    await AppServices.Stats.FlushAsync();
                    var today = AppServices.Stats.Today;
                    var path = await Task.Run(() => AppServices.Export.ExportCsv(today, today));
                    ShowInfo("已导出：" + path, false, "导出完成");
                    StartupService.RevealInExplorer(path);
                }
                catch (Exception ex)
                {
                    ShowInfo("导出失败：" + ex.Message, true, "导出失败");
                }
            };
            tray.OpenDataFolderRequested += (_, _) => StartupService.OpenFolder(AppServices.Storage.DataDirectory);
            tray.ExitRequested += async (_, _) => await RequestExitAsync();

            AppServices.AttachTray(tray);

            if (TrayIcon.TryParseHotkey(AppServices.Settings.PauseHotkey, out var modifiers, out var vk))
            {
                if (!tray.RegisterHotkey(modifiers, vk, out var error))
                {
                    ShowInfo("暂停快捷键注册失败：" + error + "。可以在设置页换一个组合键，不影响统计。",
                        InfoBarSeverity.Warning, "快捷键");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("托盘初始化异常", ex);
        }
    }

    // ---------------------------------------------------------------- 交互

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    public void NavigateTo(string tag)
    {
        var pageType = tag switch
        {
            "mouse" => typeof(MousePage),
            "keyboard" => typeof(KeyboardPage),
            "history" => typeof(HistoryPage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(DashboardPage),
        };

        if (ContentFrame.CurrentSourcePageType == pageType) return;
        ContentFrame.Navigate(pageType);

        foreach (var item in Nav.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag as string == tag)
            {
                Nav.SelectedItem = navItem;
                break;
            }
        }
    }

    private void OnTogglePauseClick(object sender, RoutedEventArgs e)
    {
        AppServices.Stats.TogglePause();
        UpdateShellUi();
    }

    private void OnPause5Click(object sender, RoutedEventArgs e)
    {
        AppServices.Stats.PauseFor(TimeSpan.FromMinutes(5));
        UpdateShellUi();
        ShowInfo("已暂停 5 分钟，倒计时结束后自动恢复统计。", false, "已暂停");
    }

    private void OnPause30Click(object sender, RoutedEventArgs e)
    {
        AppServices.Stats.PauseFor(TimeSpan.FromMinutes(30));
        UpdateShellUi();
        ShowInfo("已暂停 30 分钟，倒计时结束后自动恢复统计。", false, "已暂停");
    }

    public void ShowFromTray()
    {
        try
        {
            _appWindow?.Show();
            Activate();
            _petOverlay?.Show(AppServices.WindowHandle);
        }
        catch (Exception ex)
        {
            AppLog.Error("从托盘恢复窗口失败", ex);
        }
    }

    public async Task RequestExitAsync()
    {
        _reallyExit = true;
        _petChatTimer?.Stop();
        _petOverlay?.Dispose();
        _petOverlay = null;
        await AppServices.ShutdownAsync();
        Close();
    }

    // ---------------------------------------------------------------- 桌宠（同进程透明浮层）

    private PetOverlay? _petOverlay;
    private DispatcherQueueTimer? _petChatTimer;

    /// <summary>显示 / 隐藏桌宠浮层，并把偏好写回设置。</summary>
    public void UpdatePetPanel(bool visible)
    {
        PetToggleButton.Opacity = visible ? 1.0 : 0.55;
        AppServices.Settings.PetEnabled = visible;
        AppLog.Info("桌宠：" + (visible ? "显示" : "隐藏"));

        if (visible)
        {
            _ = ShowPetOverlayAsync();
        }
        else
        {
            _petOverlay?.Dispose();
            _petOverlay = null;
            _petChatTimer?.Stop();
        }
    }

    private async Task ShowPetOverlayAsync()
    {
        if (_petOverlay is { IsOpen: true })
        {
            _petOverlay.Show(AppServices.WindowHandle);
            return;
        }

        var overlay = new PetOverlay(AppServices.Pet, AppServices.Settings);
        overlay.Failed += (_, message) => ShowInfo(message, InfoBarSeverity.Warning, "桌宠");
        overlay.Tapped += (_, _) => SayFromStats(true);
        overlay.ScaleChanged += (_, scale) => AppServices.Settings.PetScale = scale;

        var ok = await overlay.ShowAsync(AppServices.WindowHandle, AppServices.Settings.PetScale);
        if (!ok)
        {
            overlay.Dispose();
            return;
        }

        _petOverlay = overlay;
        StartPetChatTimer();

        // 第一次见面先打个招呼
        await Task.Delay(900);
        SayFromStats(false);
    }

    private void StartPetChatTimer()
    {
        _petChatTimer ??= DispatcherQueue.CreateTimer();
        _petChatTimer.Interval = TimeSpan.FromSeconds(75);
        _petChatTimer.IsRepeating = true;
        _petChatTimer.Tick -= OnPetChatTick;
        _petChatTimer.Tick += OnPetChatTick;
        _petChatTimer.Start();
    }

    private void OnPetChatTick(DispatcherQueueTimer sender, object args)
    {
        if (_petOverlay is not { IsOpen: true }) return;
        if (!AppServices.Stats.IsCounting) return;   // 暂停/排除时别打扰
        SayFromStats(false);
    }

    /// <summary>按今天的统计生成一句话，让桌宠说出来。</summary>
    private void SayFromStats(bool tapped)
    {
        try
        {
            if (_petOverlay is null) return;

            var today = AppServices.Stats.GetSnapshot(Models.StatScope.Today);
            var keys = AppServices.Stats.GetKeyRows(Models.StatScope.Today);
            var topKey = keys.Count > 0 ? keys[0].KeyName : null;

            var line = tapped
                ? PetDialogue.ComposeTap(today, topKey)
                : PetDialogue.Compose(today, keys, DateTime.Now);

            _petOverlay.Say(line);
        }
        catch (Exception ex)
        {
            AppLog.Warn("桌宠说话失败：" + ex.Message);
        }
    }

    /// <summary>用户把桌宠拖到新位置后，记住相对主窗口的偏移。</summary>
    public void HandlePetDropped()
    {
        _petOverlay?.SyncWithOwner(AppServices.WindowHandle);
    }

    private void OnTogglePetPanelClick(object sender, RoutedEventArgs e)
        => UpdatePetPanel(_petOverlay is null);

    /// <summary>设置页换模型目录后调：重建浮层。</summary>
    public void ReloadPetPanel()
    {
        if (_petOverlay is null) return;
        _petOverlay.Dispose();
        _petOverlay = null;
        _ = ShowPetOverlayAsync();
    }

    public void ShowInfo(string message, bool isError, string title = "提示")
        => ShowInfo(message, isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational, title);

    public void ShowInfo(string message, InfoBarSeverity severity, string title = "提示")
    {
        // 事件可能来自后台线程（落盘、桌宠启动），InfoBar 只能在 UI 线程碰
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowInfo(message, severity, title));
            return;
        }

        try
        {
            GlobalInfoBar.Title = title;
            GlobalInfoBar.Message = message;
            GlobalInfoBar.Severity = severity;
            GlobalInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("显示 InfoBar 失败", ex);
        }
    }

    private void UpdateShellUi()
    {
        if (_updatingShellUi) return;
        _updatingShellUi = true;
        try
        {
            _shell.Refresh();
            var paused = _shell.IsPaused || _shell.IsExcluded;
            var brush = paused
                ? (Brush)Application.Current.Resources["StatusPausedBrush"]
                : (Brush)Application.Current.Resources["StatusRecordingBrush"];

            StatusDot.Glyph = paused ? "\uE769" : "\uE768";
            StatusDot.Foreground = brush;
            StatusText.Text = _shell.StatusText;
            StatusText.Foreground = brush;
            TitleStatusText.Text = _shell.StatusText;
            StatusDetailText.Text = string.IsNullOrEmpty(_shell.PauseCountdown)
                ? _shell.StatusDetail
                : "剩余 " + _shell.PauseCountdown;
            PauseResumeButton.Content = paused ? "恢复统计" : "暂停统计";
            AppServices.Tray?.UpdateState(paused);
        }
        catch (Exception ex)
        {
            AppLog.Error("刷新状态 UI 失败", ex);
        }
        finally
        {
            _updatingShellUi = false;
        }
    }
}
