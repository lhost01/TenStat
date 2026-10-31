# 点掉首次启动的隐私对话框（UIA），用于自动化验收。
param([string]$ProcessName = "TenStat", [string]$ButtonName = "同意并开始统计")

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Host "找不到窗口"; exit 2 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $ButtonName)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
foreach ($el in $all) {
    $pattern = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        Write-Host ("已点击: " + $ButtonName)
        exit 0
    }
}
Write-Host ("没找到可点击的按钮: " + $ButtonName)
exit 3
