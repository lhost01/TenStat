using System;

namespace TenStat.Helpers;

/// <summary>像素 → 物理距离的估算。需要用户做一次 DPI 校准（设置页）。</summary>
public static class DpiHelper
{
    /// <summary>每英寸 0.0254 米。</summary>
    private const double MetersPerInch = 0.0254;

    /// <summary>基准 DPI：96 像素 = 1 英寸。</summary>
    public const double BaseDpi = 96.0;

    /// <summary>当前窗口所在显示器的缩放系数（1.0 = 100%）。</summary>
    public static double GetScale(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return 1.0;
            var dpi = NativeMethods.GetDpiForWindow(hwnd);
            if (dpi == 0) return 1.0;
            return dpi / BaseDpi;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// 像素 → 米。calibration 是用户在设置里校准出来的系数（默认 1.0，100% 缩放、96 DPI 显示器下约为实际值）。
    /// meters = pixels / (96 * scale) * 0.0254 * calibration
    /// </summary>
    public static double PixelsToMeters(double pixels, double scale, double calibration)
    {
        if (pixels <= 0) return 0;
        if (scale <= 0) scale = 1.0;
        if (calibration <= 0) calibration = 1.0;
        var inches = pixels / (BaseDpi * scale);
        return inches * MetersPerInch * calibration;
    }

    /// <summary>显示友好的距离文本：< 1m 用厘米，≥ 1km 用公里。</summary>
    public static string FormatMeters(double meters)
    {
        if (meters <= 0) return "0 米";
        if (meters < 1) return $"{meters * 100:0} 厘米";
        if (meters < 1000) return $"{meters:0.0} 米";
        return $"{meters / 1000:0.00} 公里";
    }
}
