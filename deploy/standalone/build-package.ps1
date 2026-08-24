#Requires -Version 5.1
<#
.SYNOPSIS
  スタンドアロンPC配布用パッケージを作成する（開発者が実行）。
  Webアプリと移行ツールを self-contained（.NET不要）で発行し、インストールスクリプトと共に package\ へまとめる。
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutDir
)
$ErrorActionPreference = "Stop"
# パラメータ既定では $PSScriptRoot が未設定の場合があるため本文で解決する。
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "package" }
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutDir | Out-Null

Write-Host "== Web アプリを発行 (self-contained / single-file) =="
dotnet publish "$repo\src\PayCalcDXPro.Web\PayCalcDXPro.Web.csproj" `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $OutDir "app")
if ($LASTEXITCODE -ne 0) { throw "Web の発行に失敗しました。" }

Write-Host "== 移行ツールを発行 (self-contained / single-file) =="
dotnet publish "$repo\tools\PayCalcDXPro.Migrator\PayCalcDXPro.Migrator.csproj" `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $OutDir "migrator")
if ($LASTEXITCODE -ne 0) { throw "移行ツールの発行に失敗しました。" }

Copy-Item "$PSScriptRoot\install.ps1"            $OutDir -Force
Copy-Item "$PSScriptRoot\uninstall.ps1"          $OutDir -Force
Copy-Item "$PSScriptRoot\README.md"              $OutDir -Force
Copy-Item "$PSScriptRoot\セットアップマニュアル.md" $OutDir -Force

Write-Host ""
Write-Host "完成: $OutDir"
Write-Host "配布: このフォルダ一式（またはZip）をユーザーへ渡し、install.ps1 を実行してもらう。"
