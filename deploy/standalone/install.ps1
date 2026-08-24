#Requires -Version 5.1
<#
.SYNOPSIS
  給与計算DXPro（スタンドアロン版）を自分のPCにインストールする。管理者権限は不要（ユーザー領域に導入）。
.PARAMETER LegacyDb
  現行（デスクトップ版）の暗号化DB（PayCalc2026.db）のパス。指定すると現行データを移行し、現行保証を検証する。
.EXAMPLE
  .\install.ps1 -LegacyDb "D:\給与計算DX\DB\PayCalc2026.db"
#>
[CmdletBinding()]
param(
    [string]$LegacyDb,
    # 基点フォルダ（変更可）。直下に固定で PayCalcDXPro\browser\<年度> を作成して配置する。
    [string]$BaseDir = "C:\syuhousystem",
    [string]$Year = "2026",
    [string]$InstallDir,
    [string]$DataDir = "$env:LOCALAPPDATA\PayCalcDXPro\data"
)
$ErrorActionPreference = "Stop"
$src = $PSScriptRoot
# 基点＋固定サフィックス＋年度。-InstallDir を明示指定した場合はそれを優先する。
if (-not $InstallDir) { $InstallDir = Join-Path $BaseDir "PayCalcDXPro\browser\$Year" }

Write-Host "== 給与計算DXPro インストール =="
Write-Host "インストール先 : $InstallDir"
Write-Host "データ格納先   : $DataDir"

New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item (Join-Path $src "app")      $InstallDir -Recurse -Force
Copy-Item (Join-Path $src "migrator") $InstallDir -Recurse -Force
New-Item -ItemType Directory -Force $DataDir | Out-Null

if ($LegacyDb) {
    if (-not (Test-Path $LegacyDb)) { throw "現行DBが見つかりません: $LegacyDb" }
    Write-Host ""
    Write-Host "== 現行データを移行し、現行保証を検証します =="
    & (Join-Path $InstallDir "migrator\PayCalcDXPro.Migrator.exe") --legacy $LegacyDb --out $DataDir --force
    if ($LASTEXITCODE -ne 0) { throw "移行（現行保証）に失敗しました。中止します。" }
}
else {
    Write-Host "（現行DB未指定：空の状態でインストールします。後で移行する場合は migrator\PayCalcDXPro.Migrator.exe を実行）"
}

# デスクトップとスタートメニューにショートカットを作成
$exe = Join-Path $InstallDir "app\PayCalcDXPro.Web.exe"
$ws = New-Object -ComObject WScript.Shell
$targets = @(
    [Environment]::GetFolderPath('Desktop'),
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
)
foreach ($dir in $targets) {
    $lnkPath = Join-Path $dir '給与計算DXPro.lnk'
    $lnk = $ws.CreateShortcut($lnkPath)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = (Join-Path $InstallDir 'app')
    $lnk.Description = '給与計算DXPro ブラウザ版（スタンドアロン）'
    $lnk.Save()
}

Write-Host ""
Write-Host "✅ インストール完了。デスクトップの『給与計算DXPro』から起動してください。"
Write-Host "   起動するとローカルでサーバが立ち上がり、既定ブラウザで画面が開きます（http://localhost:5080）。"
