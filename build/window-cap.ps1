# 把目标窗口拉到前台并截图；如果前台拉不动，就用 PrintWindow 直接抓窗口内容。
# 用法: powershell -ExecutionPolicy Bypass -File window-cap.ps1 -ProcessName TenStat -Path out.png
param(
    [string]$ProcessName = "TenStat",
    [string]$Path = "F:\活动记录器\build\window.png",
    [switch]$ForegroundOnly
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;

public static class WinCap
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public static string CaptureToFile(IntPtr hwnd, string path, bool usePrintWindow)
    {
        RECT r;
        if (!GetWindowRect(hwnd, out r)) return "GetWindowRect failed";
        int w = r.Right - r.Left, hh = r.Bottom - r.Top;
        if (w <= 0 || hh <= 0) return "bad size " + w + "x" + hh;

        using (var bmp = new Bitmap(w, hh))
        {
            if (usePrintWindow)
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    // 2 = PW_RENDERFULLCONTENT
                    bool ok = PrintWindow(hwnd, hdc, 2);
                    g.ReleaseHdc(hdc);
                    if (!ok) return "PrintWindow returned false";
                }
            }
            else
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, hh));
                }
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        return "ok " + w + "x" + hh;
    }
}
"@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing, System.Windows.Forms

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Host "no window for $ProcessName"; exit 2 }

$hwnd = $proc.MainWindowHandle
Write-Host ("hwnd=" + $hwnd + " visible=" + [WinCap]::IsWindowVisible($hwnd))

[WinCap]::ShowWindow($hwnd, 9) | Out-Null          # SW_RESTORE
[WinCap]::SetWindowPos($hwnd, [WinCap]::HWND_TOPMOST, 0, 0, 0, 0, [WinCap]::SWP_NOMOVE -bor [WinCap]::SWP_NOSIZE -bor [WinCap]::SWP_SHOWWINDOW) | Out-Null
[WinCap]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 900

if ($ForegroundOnly) {
    Write-Host ([WinCap]::CaptureToFile($hwnd, $Path, $false))
} else {
    Write-Host ("printwindow: " + [WinCap]::CaptureToFile($hwnd, $Path, $true))
}
[WinCap]::SetWindowPos($hwnd, [WinCap]::HWND_NOTOPMOST, 0, 0, 0, 0, [WinCap]::SWP_NOMOVE -bor [WinCap]::SWP_NOSIZE) | Out-Null
