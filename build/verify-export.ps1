# 验收补充：设置页导出 CSV / JSON 是否真的落盘。
param(
    [string]$ProcessName = "TenStat"
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Host "找不到窗口"; exit 2 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

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

# 先切到设置页
$nav = Find-ByPattern "设置" ([System.Windows.Automation.SelectionItemPattern]::Pattern)
if ($nav) { $nav.Pattern.Select(); Start-Sleep -Milliseconds 1200 }

foreach ($label in @("导出 CSV", "导出 JSON")) {
    $hit = Find-ByPattern $label ([System.Windows.Automation.InvokePattern]::Pattern)
    if (-not $hit) { Write-Host ("  找不到按钮: " + $label); continue }
    $hit.Pattern.Invoke()
    Start-Sleep -Milliseconds 2500
    Write-Host ("  已点击: " + $label)
}

$exportDir = Join-Path $env:LOCALAPPDATA "TenStat\exports"
if (Test-Path $exportDir) {
    Get-ChildItem $exportDir | Sort-Object LastWriteTime -Descending | Select-Object -First 4 Name, Length, LastWriteTime | Format-Table -AutoSize
} else {
    Write-Host "  导出目录不存在"
}
