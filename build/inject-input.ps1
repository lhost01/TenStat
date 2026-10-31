# 安全注入一段测试输入（仅当目标窗口在前台时才执行），用于验收低级钩子是否真的在统计。
# 用法: powershell -ExecutionPolicy Bypass -File inject-input.ps1 -ProcessName TenStat
param(
    [string]$ProcessName = "TenStat",
    [switch]$Force
)

$src = @"
using System;
using System.Runtime.InteropServices;

public static class Injector
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP   = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN= 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP  = 0x0010;
    public const uint KEYEVENTF_KEYUP      = 0x0002;

    public static void LeftClick()  { mouse_event(MOUSEEVENTF_LEFTDOWN, 0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(30); mouse_event(MOUSEEVENTF_LEFTUP, 0,0,0,UIntPtr.Zero); }
    public static void RightClick() { mouse_event(MOUSEEVENTF_RIGHTDOWN,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(30); mouse_event(MOUSEEVENTF_RIGHTUP,0,0,0,UIntPtr.Zero); }
    public static void Key(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(25); keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
}
"@
Add-Type -TypeDefinition $src

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Host "目标进程没有窗口，跳过注入"; exit 2 }

$hwnd = $proc.MainWindowHandle
$fg = [Injector]::GetForegroundWindow()
Write-Host ("target=" + $hwnd + " foreground=" + $fg)
if ($fg -ne $hwnd -and -not $Force) {
    Write-Host "目标窗口不在前台，为安全起见不注入输入（避免打进别的程序）。"
    exit 3
}

$r = New-Object Injector+RECT
[Injector]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$x = $r.Left + 700
$y = $r.Top + 90

# 鼠标先横向移动（累计像素距离），再点几下
for ($i = 0; $i -lt 20; $i++) {
    [Injector]::SetCursorPos($x - 200 + $i * 15, $y + [int](10 * [Math]::Sin($i / 2.0))) | Out-Null
    Start-Sleep -Milliseconds 15
}
for ($i = 0; $i -lt 6; $i++) { [Injector]::LeftClick() }
for ($i = 0; $i -lt 3; $i++) { [Injector]::RightClick() }

# 键盘：A B C Q Z（不碰 Enter/Esc/Space，避免触发界面默认按钮）
foreach ($vk in 0x41, 0x42, 0x43, 0x51, 0x5A, 0x41, 0x42) { [Injector]::Key([byte]$vk) }

Write-Host "injected: 6 left clicks, 3 right clicks, 7 key presses, ~300px mouse path"
