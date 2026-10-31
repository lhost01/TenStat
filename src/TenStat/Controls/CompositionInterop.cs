using System;
using System.Runtime.InteropServices;
using WinRT;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace TenStat.Controls;

/// <summary>
/// Windows.UI.Composition 的桌面互操作：给指定 HWND 建一个 DirectComposition target。
/// 这是让「WebView2 composition hosting」能把画面合成到窗口上的必要一步。
/// </summary>
[ComImport]
[Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICompositorDesktopInterop
{
    void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr target);
}

internal static class CompositionInterop
{
    /// <summary>在 hwnd 上创建 DirectComposition 目标，并返回一个已经挂到目标根上的容器视觉。</summary>
    public static ContainerVisual CreateRootVisual(IntPtr hwnd, out DesktopWindowTarget target)
    {
        var compositor = new Compositor();
        var interop = GetDesktopInterop(compositor);
        interop.CreateDesktopWindowTarget(hwnd, true, out var targetPtr);

        target = GetInspectable<DesktopWindowTarget>(targetPtr);

        var root = compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = System.Numerics.Vector2.One;
        target.Root = root;

        return root;
    }

    /// <summary>
    /// CsWinRT 的投影对象不是 RCW，直接 `(ICompositorDesktopInterop)compositor` 会 InvalidCast。
    /// 这里把 ABI 指针包成 RCW 再做 QI，这是最稳的一条路。
    /// </summary>
    private static ICompositorDesktopInterop GetDesktopInterop(Compositor compositor)
    {
        var abi = MarshalInspectable<Compositor>.FromManaged(compositor);
        try
        {
            var rcw = Marshal.GetObjectForIUnknown(abi);
            return (ICompositorDesktopInterop)rcw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("拿不到 ICompositorDesktopInterop（DirectComposition 不可用）", ex);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    private static T GetInspectable<T>(IntPtr abi) where T : class
    {
        try
        {
            return MarshalInspectable<T>.FromAbi(abi);
        }
        catch
        {
            return (T)Marshal.GetObjectForIUnknown(abi);
        }
    }
}
