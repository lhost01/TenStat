using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TenStat.Helpers;

namespace TenStat.Services;

/// <summary>某个键的增量。</summary>
public readonly record struct KeyDelta(int VirtualKeyCode, long Count, DateTimeOffset LastPressedAt);

/// <summary>
/// 全局键盘低级钩子（WH_KEYBOARD_LL）。
/// 只统计“哪个虚拟键被按了多少次”，**不读取、不还原、不保存任何字符内容**：
///  - 不调用 GetKeyboardState / ToUnicode / GetAsyncKeyState 取字符；
///  - 不做输入法关联，不区分大小写，不关心 Shift 组合出的是什么字符；
///  - 存进数据库的只有 VirtualKeyCode 和按键名（例如 A、Enter、VK_6A）。
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (long Count, DateTimeOffset Last)> _pending = new();
    private readonly HashSet<int> _pressed = new();

    private NativeMethods.HookProc? _proc;
    private IntPtr _hook = IntPtr.Zero;

    public bool IsRunning => _hook != IntPtr.Zero;
    public string? LastError { get; private set; }

    /// <summary>false（默认）= 过滤长按重复，只统计首次按下；true = 每个重复消息都算一次。</summary>
    public bool CountKeyRepeat { get; set; }

    public event EventHandler<string>? Failed;

    public bool Start()
    {
        if (IsRunning) return true;
        try
        {
            _proc = HookCallback;
            var module = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, module, 0);
            if (_hook == IntPtr.Zero)
            {
                var code = Marshal.GetLastWin32Error();
                LastError = "SetWindowsHookEx(WH_KEYBOARD_LL) 失败，Win32 错误码 " + code;
                AppLog.Warn(LastError);
                Failed?.Invoke(this, LastError);
                return false;
            }

            AppLog.Info("键盘钩子已安装");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Error("键盘钩子安装异常", ex);
            Failed?.Invoke(this, LastError);
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
                AppLog.Info("键盘钩子已卸载");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("卸载键盘钩子失败", ex);
        }
        finally
        {
            _proc = null;
            lock (_gate)
            {
                _pending.Clear();
                _pressed.Clear();
            }
        }
    }

    /// <summary>取走自上次 drain 以来的按键增量并清零。</summary>
    public List<KeyDelta> Drain()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return new List<KeyDelta>(0);
            var list = new List<KeyDelta>(_pending.Count);
            foreach (var kv in _pending)
            {
                list.Add(new KeyDelta(kv.Key, kv.Value.Count, kv.Value.Last));
            }
            _pending.Clear();
            return list;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var message = (int)wParam;
                var isDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
                var isUp = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;

                if (isDown || isUp)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    var vk = (int)data.vkCode;

                    if (isDown)
                    {
                        lock (_gate)
                        {
                            // 过滤长按重复：同一个键在 keyup 之前再来 keydown，说明是系统重复
                            if (!CountKeyRepeat && !_pressed.Add(vk))
                            {
                                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                            }

                            if (CountKeyRepeat) _pressed.Add(vk);

                            var now = DateTimeOffset.Now;
                            if (_pending.TryGetValue(vk, out var agg))
                            {
                                _pending[vk] = (agg.Count + 1, now);
                            }
                            else
                            {
                                _pending[vk] = (1, now);
                            }
                        }
                    }
                    else
                    {
                        lock (_gate) { _pressed.Remove(vk); }
                    }
                }
            }
            catch
            {
                // 见 MouseHookService：P/Invoke 边界内绝不抛异常
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
