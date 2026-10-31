using System;
using System.Runtime.InteropServices;
using TenStat.Helpers;

namespace TenStat.Services;

/// <summary>
/// 托盘图标 + 全局热键宿主。自己写 Shell_NotifyIcon，不引第三方托盘库：
///  - 一个 message-only 窗口收托盘回调、WM_HOTKEY、菜单命令；
///  - 退出时 NIM_DELETE + DestroyWindow + DestroyIcon，句柄全部释放。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint TrayIconId = 1;
    private const int HotkeyId = 0xB001;

    private const int MenuShow = 1001;
    private const int MenuTogglePause = 1002;
    private const int MenuExport = 1003;
    private const int MenuOpenData = 1004;
    private const int MenuExit = 1005;

    private NativeMethods.WndProcDelegate? _wndProc;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _icon = IntPtr.Zero;
    private bool _added;
    private string _tooltip = "TenStat";
    private bool _paused;
    private bool _hotkeyRegistered;
    private readonly string _className = "TenStat.Tray." + Guid.NewGuid().ToString("N");

    public IntPtr Handle => _hwnd;
    public bool IsHotkeyRegistered => _hotkeyRegistered;

    public event EventHandler? ShowRequested;
    public event EventHandler? TogglePauseRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? OpenDataFolderRequested;
    public event EventHandler? ExitRequested;

    public bool Initialize(string iconPath)
    {
        try
        {
            _wndProc = WindowProc;
            var module = NativeMethods.GetModuleHandle(null);

            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = module,
                lpszClassName = _className,
            };

            _ = NativeMethods.RegisterClassEx(ref wc);

            _hwnd = NativeMethods.CreateWindowEx(0, _className, "TenStat", 0, 0, 0, 0, 0,
                (IntPtr)NativeMethods.HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                AppLog.Warn("托盘宿主窗口创建失败，Win32 错误码 " + Marshal.GetLastWin32Error());
                return false;
            }

            _icon = LoadAppIcon(iconPath);
            var data = BuildData();
            data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
            data.uCallbackMessage = NativeMethods.WM_TRAY_CALLBACK;
            data.hIcon = _icon;
            data.szTip = Truncate(_tooltip, 127);

            _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
            if (!_added)
            {
                AppLog.Warn("Shell_NotifyIcon(NIM_ADD) 失败，托盘图标不可用");
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("托盘初始化失败", ex);
            return false;
        }
    }

    private static IntPtr LoadAppIcon(string iconPath)
    {
        try
        {
            if (System.IO.File.Exists(iconPath))
            {
                var handle = NativeMethods.LoadImage(IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON, 16, 16,
                    NativeMethods.LR_LOADFROMFILE);
                if (handle != IntPtr.Zero) return handle;

                handle = NativeMethods.LoadImage(IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON, 32, 32,
                    NativeMethods.LR_LOADFROMFILE);
                if (handle != IntPtr.Zero) return handle;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("加载托盘图标失败：" + ex.Message);
        }

        // 兜底：系统默认应用图标（不需要释放）
        return NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION);
    }

    private NativeMethods.NOTIFYICONDATA BuildData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = TrayIconId,
    };

    public void UpdateState(bool paused, string? tooltip = null)
    {
        _paused = paused;
        _tooltip = tooltip ?? (_paused ? "TenStat — 已暂停" : "TenStat — 记录中");
        if (!_added || _hwnd == IntPtr.Zero) return;

        var data = BuildData();
        data.uFlags = NativeMethods.NIF_TIP;
        data.szTip = Truncate(_tooltip, 127);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    /// <summary>注册全局热键；重复调用会先注销旧的。</summary>
    public bool RegisterHotkey(uint modifiers, uint vk, out string? error)
    {
        error = null;
        if (_hwnd == IntPtr.Zero)
        {
            error = "托盘宿主窗口尚未创建";
            return false;
        }

        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }

        if (modifiers == 0 && vk == 0) return true;

        _hotkeyRegistered = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, vk);
        if (!_hotkeyRegistered)
        {
            error = "热键被占用或格式不合法（Win32 错误码 " + Marshal.GetLastWin32Error() + "）";
            AppLog.Warn("注册热键失败：" + error);
        }
        return _hotkeyRegistered;
    }

    public static bool TryParseHotkey(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "ALT":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "SHIFT":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "WIN":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    if (raw.Length == 1)
                    {
                        var ch = char.ToUpperInvariant(raw[0]);
                        if (ch is >= 'A' and <= 'Z')
                        {
                            vk = ch;
                        }
                        else if (ch is >= '0' and <= '9')
                        {
                            vk = ch;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else if (raw.StartsWith('F') && int.TryParse(raw[1..], out var fn) && fn is >= 1 and <= 24)
                    {
                        vk = (uint)(0x70 + fn - 1);
                    }
                    else
                    {
                        return false;
                    }
                    break;
            }
        }

        return vk != 0;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case NativeMethods.WM_TRAY_CALLBACK:
                {
                    var mouseMessage = (int)(lParam.ToInt64() & 0xFFFF);
                    if (mouseMessage == 0x0202) // WM_LBUTTONUP
                    {
                        ShowRequested?.Invoke(this, EventArgs.Empty);
                    }
                    else if (mouseMessage == 0x0205) // WM_RBUTTONUP
                    {
                        ShowContextMenu();
                    }
                    return IntPtr.Zero;
                }

                case NativeMethods.WM_HOTKEY:
                {
                    if (wParam.ToInt32() == HotkeyId)
                    {
                        TogglePauseRequested?.Invoke(this, EventArgs.Empty);
                    }
                    return IntPtr.Zero;
                }

                case NativeMethods.WM_DESTROY:
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("托盘窗口回调异常", ex);
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (_hwnd == IntPtr.Zero) return;

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr((uint)MenuShow), "显示主界面");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr((uint)MenuTogglePause), _paused ? "恢复统计" : "暂停统计");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr((uint)MenuExport), "导出今天的数据");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr((uint)MenuOpenData), "打开数据文件夹");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr((uint)MenuExit), "退出");

            NativeMethods.GetCursorPos(out var pt);

            // 必须先抢焦点，否则点菜单外面菜单不会消失
            NativeMethods.SetForegroundWindow(_hwnd);

            var command = NativeMethods.TrackPopupMenu(menu,
                NativeMethods.TPM_LEFTALIGN | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);

            switch (command)
            {
                case MenuShow:
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuTogglePause:
                    TogglePauseRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuExport:
                    ExportRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuOpenData:
                    OpenDataFolderRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("托盘菜单异常", ex);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    public void Dispose()
    {
        try
        {
            if (_added && _hwnd != IntPtr.Zero)
            {
                var data = BuildData();
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
                _added = false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("移除托盘图标失败：" + ex.Message);
        }

        try
        {
            if (_hotkeyRegistered && _hwnd != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
                _hotkeyRegistered = false;
            }
        }
        catch { }

        try
        {
            if (_icon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_icon);
                _icon = IntPtr.Zero;
            }
        }
        catch { }

        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
        catch { }

        _wndProc = null;
    }
}
