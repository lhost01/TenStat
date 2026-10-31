# 抓「桌宠浮层」窗口（独立顶层窗口，DComp 合成，PrintWindow 会黑，所以用屏幕拷贝）。
param(
    [string]$ProcessName = "TenStat",
    [string]$TitleLike = "桌宠",
    [string]$Path = "F:\活动记录器\build\pet-overlay.png"
)

Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class PetWin
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static IntPtr Find(uint pid, string titleLike)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint wpid; GetWindowThreadProcessId(h, out wpid);
            if (wpid != pid) return true;
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, 256);
            if (sb.ToString().Contains(titleLike) && IsWindowVisible(h))
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
Add-Type -TypeDefinition $src

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host "进程不存在"; exit 2 }

$hwnd = [PetWin]::Find([uint32]$proc.Id, $TitleLike)
if ($hwnd -eq [IntPtr]::Zero) { Write-Host "没找到桌宠窗口"; exit 3 }

$r = New-Object PetWin+RECT
[PetWin]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
Write-Host ("pet hwnd={0} rect=({1},{2})-({3},{4}) {5}x{6}" -f $hwnd, $r.Left, $r.Top, $r.Right, $r.Bottom, $w, $h)

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
$g.Dispose()
$bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "saved $Path"
