# 打包成一个"能直接发给别人"的绿色目录：
#   dist\TenStat.exe          ← 双击就能跑
#   dist\Assets\Live2D\...     ← 桌宠页面与渲染库
#   dist\使用说明.txt
# 另外在根目录生成 TenStat-win-x64.zip（可选，-Zip 开关）。
#
#   powershell -ExecutionPolicy Bypass -File publish.ps1
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -Zip

param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$Platform = "x64",

    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$dist = Join-Path $root "dist"

function Find-VsMSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
        if ($path) { return ($path | Select-Object -First 1) }
        $install = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($install) {
            $candidate = Join-Path $install "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $candidate) { return $candidate }
        }
    }

    foreach ($candidate in @(
        "D:\c\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )) {
        if (Test-Path $candidate) { return $candidate }
    }

    return $null
}

$msbuild = Find-VsMSBuild
if (-not $msbuild) { throw "找不到 Visual Studio 的 MSBuild.exe（WinUI 的 PRI 生成需要它）。" }

Write-Host "== 1/3 构建 $Configuration|$Platform" -ForegroundColor Cyan
& $msbuild (Join-Path $root "src\TenStat\TenStat.csproj") `
    -restore -p:Configuration=$Configuration -p:Platform=$Platform -nologo -v:m
if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }

$out = Join-Path $root "src\TenStat\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\win-x64"
if (-not (Test-Path $out)) { throw "找不到输出目录：$out" }

Write-Host "== 2/3 复制到 dist\" -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

# 运行必需的文件（不含 pdb、obj、临时文件）
Get-ChildItem $out -Recurse -File |
    Where-Object { $_.Extension -notin @('.pdb', '.xml', '.lib', '.exp') -and $_.Name -notlike '*.tmp' } |
    ForEach-Object {
        $relative = $_.FullName.Substring($out.Length).TrimStart('\')
        $target = Join-Path $dist $relative
        $dir = Split-Path -Parent $target
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Copy-Item $_.FullName $target -Force
    }

$exe = Join-Path $dist "TenStat.exe"
if (-not (Test-Path $exe)) { throw "dist 里没有 TenStat.exe，检查 csproj 的 AssemblyName" }

Write-Host "== 3/3 写说明文件" -ForegroundColor Cyan
$readme = @"
TenStat（TenStat）$Configuration
=============================================

双击「TenStat.exe」即可运行，不需要安装、不需要管理员权限、不联网。

· 数据位置：%LocalAppData%\TenStat\stats.db
· 托盘：关窗口会缩到右下角托盘；托盘菜单可显示主界面 / 暂停 / 导出 / 退出
· 桌宠：主窗口旁边那只透明的小人，点一下会说话，滚轮调大小，按住可拖动
· 卸载：删掉这个文件夹，再删 %LocalAppData%\TenStat 即可

隐私：只统计点击次数、移动距离和按键次数，不记录任何输入内容、不截屏、不上传。
"@
[System.IO.File]::WriteAllText((Join-Path $dist "使用说明.txt"), $readme, (New-Object System.Text.UTF8Encoding($true)))

$files = Get-ChildItem $dist -Recurse -File
$count = $files.Count
$size = ($files | Measure-Object Length -Sum).Sum / 1MB
Write-Host ""
Write-Host ("打包完成：" + $dist + "（" + $count + " 个文件，" + ("{0:N1}" -f $size) + " MB）") -ForegroundColor Green
Write-Host ("双击：" + $exe) -ForegroundColor Green

if ($Zip) {
    $zipPath = Join-Path $root "TenStat-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host ("已打包 zip：{0}" -f $zipPath) -ForegroundColor Green
}
