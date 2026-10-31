# 验收：暂停 → 设置页「清空今日数据」→ 二次确认 → 回仪表盘看空状态。
param([string]$ProcessName = "TenStat", [string]$OutDir = "F:\活动记录器\build\verify")

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;

public static class ClearWin
{
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

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
if (-not $proc) { Write-Host "找不到窗口"; exit 2 }
$hwnd = $proc.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

function Find-ByPattern([string]$name, $patternType, $scope = $null) {
    if (-not $scope) { $scope = $root }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $all = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($el in $all) {
        $pattern = $null
        if ($el.TryGetCurrentPattern($patternType, [ref]$pattern)) { return @{ Element = $el; Pattern = $pattern } }
    }
    return $null
}

function Click-Button([string]$name) {
    $hit = Find-ByPattern $name ([System.Windows.Automation.InvokePattern]::Pattern)
    if (-not $hit) { Write-Host ("  [miss] " + $name); return $false }
    $hit.Pattern.Invoke()
    Write-Host ("  [click] " + $name)
    return $true
}

function Select-Nav([string]$name) {
    $hit = Find-ByPattern $name ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $hit) { Write-Host ("  [miss nav] " + $name); return $false }
    $hit.Pattern.Select()
    Write-Host ("  [nav] " + $name)
    return $true
}

Write-Host "== 暂停（清空前先停掉统计，免得空状态刚显示又被填上数字）"
Click-Button "暂停统计" | Out-Null
Start-Sleep -Milliseconds 800

Write-Host "== 设置页 → 清空今日数据 → 二次确认"
Select-Nav "设置" | Out-Null
Start-Sleep -Milliseconds 1200
Click-Button "清空今日数据" | Out-Null
Start-Sleep -Milliseconds 1200

# 确认对话框在独立的 Popup 里，直接用桌面根找按钮
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$confirm = Find-ByPattern "清空今日" ([System.Windows.Automation.InvokePattern]::Pattern) $desktop
if ($confirm) { $confirm.Pattern.Invoke(); Write-Host "  [click] 清空今日（确认）" } else { Write-Host "  [miss] 确认按钮" }
Start-Sleep -Milliseconds 2000

Write-Host "== 回仪表盘看空状态"
Select-Nav "仪表盘" | Out-Null
Start-Sleep -Milliseconds 1500
Write-Host ("  shot=" + [ClearWin]::Shot($hwnd, (Join-Path $OutDir "empty-state.png")))
