using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using TenStat.Helpers;
using TenStat.Services;
using Windows.Foundation;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace TenStat.Controls;

/// <summary>
/// 桌宠浮层：一个**同进程**的无边框透明窗口，里面用 WebView2 的 composition hosting 渲染 Live2D。
///
/// 为什么不做在主窗口的 XAML 里：
///   WinUI 3 的 WebView2 控件走 windowed hosting，拿不到逐像素透明（背景永远是一整块白/黑）。
///   要做到「没有背景色」，只能自己建窗口 + DirectComposition target + CoreWebView2CompositionController，
///   在 composition 模式下 `DefaultBackgroundColor = Transparent` 才真正有效。
///
/// 交互（全部由宿主处理，不依赖页面收鼠标）：
///   拖动窗口、单击说话 + 换表情、滚轮缩放、位置按「相对主窗口的偏移」记住。
/// </summary>
public sealed class PetOverlay : IDisposable
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_DESTROY = 0x0002;
    private const int WM_PAINT = 0x000F;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;

    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref NativeMethods.WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private readonly Live2DPetService _pet;
    private readonly SettingsService _settings;

    private NativeMethods.WndProcDelegate? _wndProc;
    private string? _className;
    private IntPtr _hwnd = IntPtr.Zero;
    private CoreWebView2CompositionController? _controller;
    private DesktopWindowTarget? _target;
    private ContainerVisual? _root;

    private bool _dragging;
    private POINT _dragOrigin;
    private RECT _dragStartRect;
    private int _movedPixels;

    private double _scale = 1.0;
    private int _baseWidth = 240;
    private int _baseHeight = 380;

    private IntPtr _owner;
    private bool _stayInside = true;
    private bool _shown = true;
    private DispatcherQueueTimer? _tick;

    public PetOverlay(Live2DPetService pet, SettingsService settings)
    {
        _pet = pet;
        _settings = settings;
    }

    public bool IsOpen => _hwnd != IntPtr.Zero;
    public IntPtr Handle => _hwnd;

    public event EventHandler? Tapped;
    public event EventHandler<string>? Failed;
    public event EventHandler<double>? ScaleChanged;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    // ---------------------------------------------------------------- 创建 / 销毁

    public async Task<bool> ShowAsync(IntPtr owner, double scale)
    {
        if (IsOpen) return true;

        try
        {
            _scale = Math.Clamp(scale, 0.6, 1.8);
            _owner = owner;
            _stayInside = _settings.PetStayInsideApp;
            CreateWindow(owner);
            if (_hwnd == IntPtr.Zero)
            {
                Fail("桌宠窗口创建失败");
                return false;
            }

            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            ApplyBounds();

            if (!await InitializeWebViewAsync())
            {
                return false;
            }

            StartTicker();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("桌宠浮层创建失败", ex);
            Fail("桌宠浮层创建失败：" + ex.Message);
            return false;
        }
    }

    private void CreateWindow(IntPtr owner)
    {
        _wndProc = WindowProc;
        _className = "TenStat.Pet." + Guid.NewGuid().ToString("N");
        var module = NativeMethods.GetModuleHandle(null);

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = 0x0002 | 0x0001, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = module,
            lpszClassName = _className,
            hCursor = LoadArrowCursor(),
        };
        _ = RegisterClassEx(ref wc);

        GetDefaultPosition(owner, out var x, out var y);
        var (w, h) = CurrentPixelSize();

        _hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP | WS_EX_NOACTIVATE,
            _className, "TenStat · 桌宠", WS_POPUP,
            x, y, w, h, owner, IntPtr.Zero, module, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    private static IntPtr LoadArrowCursor() => LoadCursor(IntPtr.Zero, new IntPtr(32512));

    private (int Width, int Height) CurrentPixelSize()
    {
        var dpi = _hwnd == IntPtr.Zero ? 96 : (int)GetDpiForWindow(_hwnd);
        if (dpi <= 0) dpi = 96;
        var factor = dpi / 96.0;
        return ((int)Math.Round(_baseWidth * _scale * factor), (int)Math.Round(_baseHeight * _scale * factor));
    }

    private void GetDefaultPosition(IntPtr owner, out int x, out int y)
    {
        GetWindowRect(owner, out var rect);
        var (w, h) = CurrentPixelSize();

        var ownerWidth = rect.Right - rect.Left;
        var ownerHeight = rect.Bottom - rect.Top;

        var offsetX = _settings.PetX;
        var offsetY = _settings.PetY;

        // 只在偏移"落在主窗口范围内"时才沿用（防止旧版本留下的脏值把桌宠甩到屏幕角落）
        var usable = offsetX >= 0 && offsetY >= 0
                                  && offsetX <= Math.Max(0, ownerWidth - 80)
                                  && offsetY <= Math.Max(0, ownerHeight - 80);

        if (usable)
        {
            x = rect.Left + offsetX;
            y = rect.Top + offsetY;
        }
        else
        {
            // 默认：主窗口右下角内侧
            x = rect.Right - w - 24;
            y = rect.Bottom - h - 16;
        }

        // 无论哪种，都别跑出屏幕
        var screenW = GetSystemMetrics(0);
        var screenH = GetSystemMetrics(1);
        x = Math.Clamp(x, -w + 60, Math.Max(0, screenW - 60));
        y = Math.Clamp(y, 0, Math.Max(0, screenH - 60));
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private void ApplyBounds()
    {
        if (_hwnd == IntPtr.Zero) return;

        var (w, h) = CurrentPixelSize();
        GetWindowRect(_hwnd, out var rect);
        SetWindowPos(_hwnd, IntPtr.Zero, rect.Left, rect.Top, w, h, SWP_NOACTIVATE | SWP_NOZORDER);

        if (_controller is not null)
        {
            var dpi = (int)GetDpiForWindow(_hwnd);
            if (dpi <= 0) dpi = 96;
            _controller.RasterizationScale = dpi / 96.0;
            _controller.Bounds = new Rect(0, 0, w, h);
        }
    }

    // ---------------------------------------------------------------- WebView2

    private async Task<bool> InitializeWebViewAsync()
    {
        if (!_pet.IsPageAvailable)
        {
            Fail("缺少 Assets\\Live2D\\pet.html");
            return false;
        }

        var modelDir = _pet.ResolveModelDirectory();
        if (string.IsNullOrEmpty(modelDir))
        {
            Fail("没找到 Live2D 模型，到 设置 → 桌宠 → 选择目录 指一下模型文件夹。");
            return false;
        }

        var modelFile = _pet.ResolveModelFile(modelDir);
        if (modelFile is null)
        {
            Fail("模型目录里没有 *.model3.json：" + modelDir);
            return false;
        }

        var environment = await _pet.GetEnvironmentAsync();
        if (environment is null)
        {
            Fail("WebView2 初始化失败，检查是否安装了 WebView2 Runtime。");
            return false;
        }

        // 关键：composition hosting + DirectComposition target，才能逐像素透明
        _root = CompositionInterop.CreateRootVisual(_hwnd, out var target);
        _target = target;

        _controller = await environment.CreateCoreWebView2CompositionControllerAsync(
            CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)_hwnd.ToInt64()));
        _controller.RootVisualTarget = _root;
        _controller.DefaultBackgroundColor = Colors.Transparent;

        var core = _controller.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        core.SetVirtualHostNameToFolderMapping("app.local", _pet.AppAssetDirectory, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("model.local", modelDir, CoreWebView2HostResourceAccessKind.Allow);

        var settingsJson = _pet.TryBuildSettingsJson(modelDir, modelFile, out var diagnostics);
        if (settingsJson is null)
        {
            Fail("模型配置读取失败：" + diagnostics);
            return false;
        }

        AppLog.Info("桌宠模型：" + System.IO.Path.Combine(modelDir, modelFile) + "（" + diagnostics + "）");

        var literal = System.Text.Json.JsonSerializer.Serialize(settingsJson);
        await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__PET_SETTINGS_JSON__ = " + literal + ";");

        core.WebMessageReceived += OnWebMessage;
        core.ProcessFailed += (_, args) => Fail("渲染进程异常：" + args.ProcessFailedKind);

        // 诊断：确认页面底色是否真的透明（composition 模式下 Transparent 才生效）
        core.NavigationCompleted += async (_, _) =>
        {
            try
            {
                var probe = await core.ExecuteScriptAsync(
                    "JSON.stringify({body:getComputedStyle(document.body).backgroundColor," +
                    "html:getComputedStyle(document.documentElement).backgroundColor," +
                    "canvas:(function(){var c=document.querySelector('canvas');return c?getComputedStyle(c).backgroundColor:'none';})()})");
                AppLog.Info("桌宠底色探测：" + probe);

            }
            catch (Exception ex)
            {
                AppLog.Warn("底色探测失败：" + ex.Message);
            }
        };

        var dpi = (int)GetDpiForWindow(_hwnd);
        if (dpi <= 0) dpi = 96;
        _controller.RasterizationScale = dpi / 96.0;
        var (w, h) = CurrentPixelSize();
        _controller.Bounds = new Rect(0, 0, w, h);

        core.Navigate("https://app.local/pet.html?model=%2F" + Uri.EscapeDataString(modelFile));
        return true;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string text;
        try { text = e.WebMessageAsJson; } catch { return; }
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("type", out var typeElement)) return;
            var type = typeElement.GetString();

            switch (type)
            {
                case "ready":
                    AppLog.Info("桌宠已就绪（composition / 透明）");
                    break;
                case "error":
                    Fail("桌宠加载失败：" + (doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "未知"));
                    break;
                case "log":
                    if (doc.RootElement.TryGetProperty("message", out var log)) AppLog.Info("[pet] " + log.GetString());
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("解析桌宠消息失败：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 交互

    /// <summary>让页面显示一句话（气泡），并顺手换个表情/动作。</summary>
    public void Say(string text)
    {
        SendCommand("say", "\"text\":" + System.Text.Json.JsonSerializer.Serialize(text));
        if (DateTime.UtcNow.Ticks % 2 == 0)
        {
            SendCommand("motion", "\"group\":\"TapBody\"");
        }
        else
        {
            SendCommand("expression", "\"name\":" + System.Text.Json.JsonSerializer.Serialize(RandomExpression()));
        }
    }

    private string _lastExpression = string.Empty;

    private string RandomExpression()
    {
        var names = _pet.ExpressionNames;
        if (names.Count == 0) return string.Empty;
        _lastExpression = names[Random.Shared.Next(names.Count)];
        return _lastExpression;
    }

    private void SendCommand(string cmd, string extraJson)
    {
        try
        {
            _controller?.CoreWebView2?.PostWebMessageAsJson("{\"cmd\":\"" + cmd + "\"" + (extraJson.Length > 0 ? "," + extraJson : "") + "}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("发送桌宠指令失败：" + ex.Message);
        }
    }

    public void SetScale(double scale)
    {
        _scale = Math.Clamp(scale, 0.6, 1.8);
        ApplyBounds();
        ScaleChanged?.Invoke(this, _scale);
    }

    /// <summary>跟随主窗口移动（保持相对偏移）。</summary>
    public void SyncWithOwner(IntPtr owner)
    {
        if (!IsOpen) return;
        GetWindowRect(owner, out var ownerRect);
        GetWindowRect(_hwnd, out var petRect);

        var offsetX = petRect.Left - ownerRect.Left;
        var offsetY = petRect.Top - ownerRect.Top;

        // 主窗口移动时，把桌面宠物一起挪过去（用保存的偏移，而不是它自己的绝对位置）
        if (_lastOwnerRect.Left == 0 && _lastOwnerRect.Top == 0)
        {
            _lastOwnerRect = ownerRect;
            return;
        }

        var dx = ownerRect.Left - _lastOwnerRect.Left;
        var dy = ownerRect.Top - _lastOwnerRect.Top;
        _lastOwnerRect = ownerRect;

        if (dx == 0 && dy == 0) return;

        SetWindowPos(_hwnd, IntPtr.Zero, petRect.Left + dx, petRect.Top + dy, 0, 0,
            SWP_NOACTIVATE | SWP_NOZORDER | 0x0001 /* SWP_NOSIZE */);

        SaveOffset(ownerRect);
    }

    private RECT _lastOwnerRect;

    private void SaveOffset(RECT ownerRect)
    {
        if (!IsOpen) return;
        GetWindowRect(_hwnd, out var petRect);
        _settings.PetX = petRect.Left - ownerRect.Left;
        _settings.PetY = petRect.Top - ownerRect.Top;
    }

    /// <summary>「只在本程序内显示」开关。</summary>
    public void SetStayInside(bool stayInside)
    {
        _stayInside = stayInside;
        if (!stayInside)
        {
            SetVisible(true);
        }
    }

    private void StartTicker()
    {
        _tick ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_tick is null) return;

        _tick.Interval = TimeSpan.FromMilliseconds(150);
        _tick.IsRepeating = true;
        _tick.Tick -= OnTick;
        _tick.Tick += OnTick;
        _tick.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_hwnd == IntPtr.Zero) return;

        if (_stayInside)
        {
            UpdateAutoHide();
        }

        if (_shown)
        {
            UpdateFocus();
        }

    }

    /// <summary>让模型盯着鼠标指针：把全局光标换算成页面（CSS 像素）坐标发过去。</summary>
    private void UpdateFocus()
    {
        try
        {
            if (!GetCursorPos(out var cursor)) return;
            GetWindowRect(_hwnd, out var rect);

            var dpi = (int)GetDpiForWindow(_hwnd);
            var factor = dpi <= 0 ? 1.0 : dpi / 96.0;

            var x = (cursor.X - rect.Left) / factor;
            var y = (cursor.Y - rect.Top) / factor;

            SendCommand("focus", "\"x\":" + x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                                 + ",\"y\":" + y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            AppLog.Warn("同步桌宠视线失败：" + ex.Message);
        }
    }

    /// <summary>限制在程序内：主窗口不在前台（例如在玩游戏）就把桌宠藏起来。</summary>
    private void UpdateAutoHide()
    {
        try
        {
            if (_owner == IntPtr.Zero) return;

            var ownerVisible = IsWindowVisible(_owner);
            var foreground = GetForegroundWindow();
            var ownerActive = foreground == _owner || foreground == _hwnd;

            SetVisible(ownerVisible && ownerActive);
        }
        catch
        {
            // 前台探测失败就别折腾了
        }
    }

    private void SetVisible(bool visible)
    {
        if (_hwnd == IntPtr.Zero || _shown == visible) return;

        ShowWindow(_hwnd, visible ? SW_SHOWNOACTIVATE : 0);
        _shown = visible;
    }

    public void Hide() => SetVisible(false);

    public void Show(IntPtr owner)
    {
        if (_hwnd == IntPtr.Zero) return;
        _owner = owner;
        SetVisible(true);
        GetWindowRect(owner, out _lastOwnerRect);
    }

    // ---------------------------------------------------------------- 窗口过程

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_LBUTTONDOWN:
                    SetCapture(hwnd);
                    _dragging = true;
                    _movedPixels = 0;
                    _dragOrigin = CursorPos();
                    GetWindowRect(hwnd, out _dragStartRect);
                    return IntPtr.Zero;

                case WM_MOUSEMOVE:
                    if (_dragging)
                    {
                        var p = CursorPos();
                        var dx = p.X - _dragOrigin.X;
                        var dy = p.Y - _dragOrigin.Y;
                        _movedPixels = Math.Max(_movedPixels, Math.Abs(dx) + Math.Abs(dy));

                        var x = _dragStartRect.Left + dx;
                        var y = _dragStartRect.Top + dy;

                        // 「只在本程序内」：拖动范围也夹在主窗口里（允许露出一点，别把人卡死）
                        if (_stayInside && _owner != IntPtr.Zero && GetWindowRect(_owner, out var ownerRect))
                        {
                            x = Math.Clamp(x, ownerRect.Left - 40, Math.Max(ownerRect.Left - 40, ownerRect.Right - 60));
                            y = Math.Clamp(y, ownerRect.Top, Math.Max(ownerRect.Top, ownerRect.Bottom - 60));
                        }

                        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                            SWP_NOACTIVATE | SWP_NOZORDER | 0x0001 /* SWP_NOSIZE */);
                    }
                    return IntPtr.Zero;

                case WM_LBUTTONUP:
                    if (_dragging)
                    {
                        _dragging = false;
                        ReleaseCapture();
                        AppServices.MainWindow?.HandlePetDropped();

                        if (_movedPixels <= 4)
                        {
                            Tapped?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    return IntPtr.Zero;

                case WM_MOUSEWHEEL:
                {
                    var delta = (short)((long)wParam >> 16);
                    var next = Math.Clamp(_scale * (delta > 0 ? 1.08 : 0.92), 0.6, 1.8);
                    _settings.PetScale = next;
                    SetScale(next);
                    return IntPtr.Zero;
                }

                case WM_NCHITTEST:
                    return (IntPtr)1; // HTCLIENT：所有鼠标消息都归我们处理

                case WM_NCLBUTTONDBLCLK:
                    return IntPtr.Zero;

                case WM_DESTROY:
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("桌宠窗口回调异常", ex);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private static POINT CursorPos()
    {
        GetCursorPos(out var p);
        return p;
    }

    private void Fail(string message)
    {
        AppLog.Warn("桌宠：" + message);
        Failed?.Invoke(this, message);
    }

    public void Dispose()
    {
        try
        {
            if (_tick is not null)
            {
                _tick.Stop();
                _tick.Tick -= OnTick;
                _tick = null;
            }
        }
        catch { }

        try
        {
            _controller?.Close();
            _controller = null;
        }
        catch { }

        try
        {
            _root?.Dispose();
            _root = null;
            _target?.Dispose();
            _target = null;
        }
        catch { }

        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
        catch { }

        _wndProc = null;
    }
}
