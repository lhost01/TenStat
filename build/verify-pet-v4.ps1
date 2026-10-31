# 验收：桌宠视线跟随 + 「只在本程序内显示」开关
param(
    [string]$ProcessName = "TenStat",
    [string]$OutDir = "F:\活动记录器\build\v4"
)

Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;

public static class PetTest
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public static IntPtr FindExact(uint pid, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            if (sb.ToString() == title && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr Foreground()
    {
        return GetForegroundWindow();
    }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

    public static IntPtr Find(uint pid, string like)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            if (sb.ToString().Contains(like) && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Shot(IntPtr h, string path)
    {
        RECT r; GetWindowRect(h, out r);
        int w = r.Right - r.Left, hh = r.Bottom - r.Top;
        if (w <= 0 || hh <= 0) return "bad size";
        using (var bmp = new Bitmap(w, hh))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, hh));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        return w + "x" + hh;
    }
}
"@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host "进程不存在"; exit 2 }

$pet = [PetTest]::Find([uint32]$proc.Id, '桌宠')
$main = [PetTest]::FindExact([uint32]$proc.Id, 'TenStat')
if ($pet -eq [IntPtr]::Zero) { Write-Host "没找到桌宠窗口"; exit 3 }
Write-Host ("pet=" + $pet + " main=" + $main)

$r = New-Object PetTest+RECT
[PetTest]::GetWindowRect($pet, [ref]$r) | Out-Null

$old = New-Object PetTest+POINT
[PetTest]::GetCursorPos([ref]$old) | Out-Null
Write-Host ("原光标 " + $old.X + "," + $old.Y)

Write-Host '--- 视线：指针放左侧 ---'
[PetTest]::SetCursorPos($r.Left - 260, $r.Top + 120) | Out-Null
Start-Sleep -Milliseconds 700
Write-Host ([PetTest]::Shot($pet, (Join-Path $OutDir 'focus-left.png')))

Write-Host '--- 视线：指针放右侧 ---'
[PetTest]::SetCursorPos($r.Right + 260, $r.Top + 120) | Out-Null
Start-Sleep -Milliseconds 700
Write-Host ([PetTest]::Shot($pet, (Join-Path $OutDir 'focus-right.png')))

Write-Host '--- 只在本程序内：最小化主窗口（模拟切到别的应用）---'
[PetTest]::ShowWindow($main, 6) | Out-Null   # SW_MINIMIZE
Start-Sleep -Milliseconds 1200
Write-Host ("最小化后 pet 可见 = " + [PetTest]::IsWindowVisible($pet))
Write-Host ("          前台 hwnd = " + [PetTest]::Foreground())

Write-Host '--- 恢复主窗口 ---'
[PetTest]::ShowWindow($main, 9) | Out-Null    # SW_RESTORE
[PetTest]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 1200
Write-Host ("恢复后 pet 可见 = " + [PetTest]::IsWindowVisible($pet))
Write-Host ("          前台 hwnd = " + [PetTest]::Foreground())

[PetTest]::SetCursorPos($old.X, $old.Y) | Out-Null
Write-Host 'done'
