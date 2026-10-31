# 自动化验收：切换统计口径、暂停/恢复、关窗到托盘。全部用 UI Automation + Win32，不需要真实点鼠标。
param(
    [string]$ProcessName = "TenStat",
    [string]$OutDir = "F:\活动记录器\build\verify"
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;

public static class VerWin
{
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static string Shot(IntPtr hwnd, string path)
    {
        RECT r;
        if (!GetWindowRect(hwnd, out r)) return "GetWindowRect failed";
        int w = r.Right - r.Left, hh = r.Bottom - r.Top;
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
        return "ok";
    }
}
"@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Host "找不到窗口，先启动程序"; exit 2 }
$hwnd = $proc.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

function Find-ByPattern([string]$name, $patternType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($el in $all) {
        $pattern = $null
        if ($el.TryGetCurrentPattern($patternType, [ref]$pattern)) { return @{ Element = $el; Pattern = $pattern } }
    }
    return $null
}

function Select-Radio([string]$name) {
    $hit = Find-ByPattern $name ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($hit) { $hit.Pattern.Select(); return $true }
    return $false
}

function Invoke-Button([string]$name) {
    $hit = Find-ByPattern $name ([System.Windows.Automation.InvokePattern]::Pattern)
    if ($hit) { $hit.Pattern.Invoke(); return $true }
    return $false
}

Write-Host "== 口径切换 =="
foreach ($scope in @(@{n="本次会话"; f="scope-session"}, @{n="总计"; f="scope-total"}, @{n="今日"; f="scope-today"})) {
    $ok = Select-Radio $scope.n
    Start-Sleep -Milliseconds 1200
    $shot = [VerWin]::Shot($hwnd, (Join-Path $OutDir ($scope.f + ".png")))
    Write-Host ("  {0,-6} select={1} shot={2}" -f $scope.n, $ok, $shot)
}

Write-Host "== 暂停 / 恢复 =="
$paused = Invoke-Button "暂停统计"
Start-Sleep -Milliseconds 1500
$shot1 = [VerWin]::Shot($hwnd, (Join-Path $OutDir "paused.png"))
Write-Host ("  暂停 invoke={0} shot={1}" -f $paused, $shot1)
$resumed = Invoke-Button "恢复统计"
Start-Sleep -Milliseconds 1200
$shot2 = [VerWin]::Shot($hwnd, (Join-Path $OutDir "resumed.png"))
Write-Host ("  恢复 invoke={0} shot={1}" -f $resumed, $shot2)

Write-Host "== 关窗到托盘 =="
[VerWin]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # WM_CLOSE
Start-Sleep -Milliseconds 1500
$alive = -not $proc.HasExited
$visible = [VerWin]::IsWindowVisible($hwnd)
Write-Host ("  进程存活={0} 窗口可见={1}" -f $alive, $visible)
if ($alive -and -not $visible) { Write-Host "  结论：关窗后进程仍在、窗口隐藏（最小化到托盘）" }
} elseif (-not $alive) { Write-Host "  结论：关窗后进程退出了（需要检查 MinimizeToTray 设置）" }
else { Write-Host "  结论：窗口没有隐藏，需要检查 Closing 逻辑" }

if ($alive) { [VerWin]::ShowWindow($hwnd, 5) | Out-Null }
