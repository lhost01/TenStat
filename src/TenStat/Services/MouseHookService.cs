using System;
using System.Runtime.InteropServices;
using TenStat.Helpers;

namespace TenStat.Services;

/// <summary>一次 drain 拿到的鼠标增量。</summary>
public readonly record struct MouseDelta(long LeftClicks, long RightClicks, double MovePixels);

/// <summary>
/// 全局鼠标低级钩子（WH_MOUSE_LL）。
/// 纪律：
///  - 回调里只做加法和一次欧氏距离，绝不写库、不碰 UI；
///  - 回调运行在装了钩子的那个线程（主线程）上，仍然用 Interlocked 保护，因为 drain 可能来自别的线程；
///  - 单次位移 &gt; 2000px 视为异常跳变（切换显示器、远程桌面、鼠标加速等），不计入。
/// </summary>
public sealed class MouseHookService : IDisposable
{
    private const double MaxJumpPixels = 2000.0;

    private readonly object _gate = new();
    private NativeMethods.HookProc? _proc;
    private IntPtr _hook = IntPtr.Zero;

    private long _left;
    private long _right;
    private double _pixels;
    private NativeMethods.POINT _lastPoint;
    private bool _hasLastPoint;

    public bool IsRunning => _hook != IntPtr.Zero;
    public string? LastError { get; private set; }
    public long DroppedJumps { get; private set; }

    /// <summary>安装失败时抛出（一般是权限或全局钩子被策略拦截）。</summary>
    public event EventHandler<string>? Failed;

    public bool Start()
    {
        if (IsRunning) return true;
        try
        {
            _proc = HookCallback;
            var module = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, module, 0);
            if (_hook == IntPtr.Zero)
            {
                var code = Marshal.GetLastWin32Error();
                LastError = "SetWindowsHookEx(WH_MOUSE_LL) 失败，Win32 错误码 " + code;
                AppLog.Warn(LastError);
                Failed?.Invoke(this, LastError);
                return false;
            }

            _hasLastPoint = false;
            AppLog.Info("鼠标钩子已安装");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Error("鼠标钩子安装异常", ex);
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
                AppLog.Info("鼠标钩子已卸载");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("卸载鼠标钩子失败", ex);
        }
        finally
        {
            _proc = null;
        }
    }

    /// <summary>取走自上次 drain 以来的增量并清零（UI 线程每 300ms 调一次）。</summary>
    public MouseDelta Drain()
    {
        lock (_gate)
        {
            var delta = new MouseDelta(Interlocked.Exchange(ref _left, 0),
                                       Interlocked.Exchange(ref _right, 0),
                                       _pixels);
            _pixels = 0;
            return delta;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var message = (int)wParam;
                switch (message)
                {
                    case NativeMethods.WM_LBUTTONDOWN:
                        Interlocked.Increment(ref _left);
                        break;

                    case NativeMethods.WM_RBUTTONDOWN:
                        Interlocked.Increment(ref _right);
                        break;

                    case NativeMethods.WM_MOUSEMOVE:
                        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                        lock (_gate)
                        {
                            if (_hasLastPoint)
                            {
                                var dx = (double)data.pt.X - _lastPoint.X;
                                var dy = (double)data.pt.Y - _lastPoint.Y;
                                var distance = Math.Sqrt(dx * dx + dy * dy);
                                if (distance > 0 && distance < MaxJumpPixels)
                                {
                                    _pixels += distance;
                                }
                                else if (distance >= MaxJumpPixels)
                                {
                                    DroppedJumps++;
                                }
                            }
                            _lastPoint = data.pt;
                            _hasLastPoint = true;
                        }
                        break;
                }
            }
            catch
            {
                // 钩子回调里吞掉一切异常：绝不能让异常穿过 P/Invoke 边界
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
