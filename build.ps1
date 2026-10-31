# 一键构建（WinUI 3 主程序）。
#
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Configuration Debug -Run
#
# 为什么必须用 Visual Studio 的 MSBuild：
#   WinUI 3 的 MRT/PRI 生成需要 Microsoft.Build.Packaging.Pri.Tasks.dll，
#   这个程序集只随 Visual Studio 安装，dotnet SDK 里没有。
#   （纯 CLI 会报 MSB4062 "未能加载任务 ExpandPriContent"。）

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Platform = "x64",

    [switch]$Run
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

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
if (-not $msbuild) {
    throw @"
找不到 Visual Studio 的 MSBuild.exe。
请安装 Visual Studio 2022（勾选「WinUI 应用程序开发」工作负载），或手动执行：
  <VS 安装目录>\MSBuild\Current\Bin\MSBuild.exe src\TenStat\TenStat.csproj -restore -p:Configuration=$Configuration -p:Platform=$Platform
"@
}

Write-Host "== 构建 TenStat（WinUI 3，含内嵌 Live2D 桌宠）" -ForegroundColor Cyan
Write-Host "   使用 $msbuild"

& $msbuild (Join-Path $root "src\TenStat\TenStat.csproj") `
    -restore -p:Configuration=$Configuration -p:Platform=$Platform -nologo -v:m
if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }

$exe = Join-Path $root "src\TenStat\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\win-x64\TenStat.exe"
Write-Host ""
Write-Host "构建完成：$exe" -ForegroundColor Green

if ($Run) {
    if (-not (Test-Path $exe)) { throw "找不到输出：$exe" }
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
    Write-Host "已启动。" -ForegroundColor Green
}
