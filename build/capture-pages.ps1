# 用 UI Automation 逐个点开导航项并抓窗口截图（不需要抢前台、不需要真实鼠标）。
# 用法: powershell -ExecutionPolicy Bypass -File capture-pages.ps1 -ProcessName TenStat -OutDir F:\活动记录器\build\shots
param(
    [string]$ProcessName = "TenStat",
    [string]$OutDir = "F:\活动记录器\build\shots"
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$shotSrc = @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;

public static class WinShot
{
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static string Shot(IntPtr hwnd, string path)
    {
        RECT r;
        if (!GetWindowRect(hwnd, out r)) return "GetWindowRect failed";
        int w = r.Right - r.Left, hh = r.Bottom - r.Top;
        if (w <= 0 || hh <= 0) return "bad size";
        using (var bmp = new Bitmap(w, hh))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok = PrintWindow(hwnd, hdc, 2);
                g.ReleaseHdc(hdc);
                if (!ok) return "PrintWindow false";
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        return "ok " + w + "x" + hh;
    }
}
"@
Add-Type -TypeDefinition $shotSrc -ReferencedAssemblies System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Host "找不到窗口"; exit 2 }
$hwnd = $proc.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
if (-not $root) { Write-Host "UIA 取不到根元素"; exit 3 }

function Select-NavItem([string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($el in $all) {
        $pattern = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            $pattern.Select()
            return $true
        }
        if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            $pattern.Invoke()
            return $true
        }
    }
    return $false
}

$pages = @(
    @{ Key = "1-dashboard"; Name = "仪表盘" },
    @{ Key = "2-mouse";     Name = "鼠标统计" },
    @{ Key = "3-keyboard";  Name = "键盘统计" },
    @{ Key = "4-history";   Name = "历史记录" },
    @{ Key = "5-settings";  Name = "设置" },
    @{ Key = "6-about";     Name = "关于" }
)

foreach ($page in $pages) {
    $ok = Select-NavItem $page.Name
    Start-Sleep -Milliseconds 1400
    $path = Join-Path $OutDir ($page.Key + ".png")
    $result = [WinShot]::Shot($hwnd, $path)
    Write-Host ("{0,-12} select={1} shot={2}" -f $page.Name, $ok, $result)
}
