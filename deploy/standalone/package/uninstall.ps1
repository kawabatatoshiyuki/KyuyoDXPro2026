#Requires -Version 5.1
<#
.SYNOPSIS
  給与計算DXPro（スタンドアロン版）をアンインストールする。既定では給与データ（DB・鍵）は残す。
.PARAMETER RemoveData
  指定すると給与データ（DataDir）も削除する（取り消し不可。注意）。
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\PayCalcDXPro",
    [string]$DataDir = "$env:LOCALAPPDATA\PayCalcDXPro\data",
    [switch]$RemoveData
)
$ErrorActionPreference = "Stop"

# ショートカット削除
$targets = @(
    [Environment]::GetFolderPath('Desktop'),
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
)
foreach ($dir in $targets) {
    $lnk = Join-Path $dir '給与計算DXPro.lnk'
    if (Test-Path $lnk) { Remove-Item $lnk -Force }
}

if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
Write-Host "アプリ本体を削除しました: $InstallDir"

if ($RemoveData) {
    if (Test-Path $DataDir) { Remove-Item $DataDir -Recurse -Force }
    Write-Host "給与データも削除しました: $DataDir"
}
else {
    Write-Host "給与データは保持しています: $DataDir （完全削除は -RemoveData）"
}
