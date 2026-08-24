#Requires -Version 5.1
<#
.SYNOPSIS
  配布用の GUI セットアップ（Setup.exe）を作成する（開発者が実行）。
  勤怠管理DXPro の build-final.bat を参考にした PowerShell 版。

  やること：
    1) self-contained 発行物（app\ / migrator\）を用意（build-package.ps1 を呼ぶ）。
    2) DXPro2026.png から PayCalcDXPro.ico を生成（可能なら）。
    3) PayCalcDXProSetup.cs を .NET Framework の csc.exe で Setup.exe にコンパイル。
    4) 配布フォルダ「installer\」に Setup.exe＋app\＋migrator\＋ico＋README をまとめる。

  外部ツール不要（Inno Setup 等は不要）。csc.exe は Windows 同梱の .NET Framework を使う。
.PARAMETER SkipPublish
  発行をスキップし、既存の package\app / package\migrator を流用する（Setup.exe だけ作り直す時に速い）。
.EXAMPLE
  .\build-setup.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repo = (Resolve-Path "$here\..\..").Path
$pkg  = Join-Path $here "package"
$out  = Join-Path $here "installer"

# ── 1) 発行物（app / migrator）を用意 ─────────────────────────────
if (-not $SkipPublish) {
    Write-Host "== self-contained 発行（build-package.ps1）==" -ForegroundColor Cyan
    & (Join-Path $here "build-package.ps1") -Configuration $Configuration -Runtime $Runtime -OutDir $pkg
}
if (-not (Test-Path (Join-Path $pkg "app\PayCalcDXPro.Web.exe"))) {
    throw "発行物が見つかりません: $pkg\app\PayCalcDXPro.Web.exe（先に build-package.ps1 を実行してください）"
}

# ── 2) アイコン生成（DXPro2026.png → PayCalcDXPro.ico）─────────────
# ICO に PNG フレームを1枚埋め込む（Vista+ 対応）。失敗しても続行（アプリexeのアイコンで代替）。
$png = Join-Path $repo "src\PayCalcDXPro.Web\wwwroot\img\DXPro2026.png"
$ico = Join-Path $here "PayCalcDXPro.ico"
try {
    Add-Type -AssemblyName System.Drawing
    $srcImg = [System.Drawing.Image]::FromFile($png)
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($srcImg, 0, 0, 256, 256)
    $g.Dispose(); $srcImg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngBytes = $ms.ToArray(); $ms.Dispose()

    $fs = [System.IO.File]::Create($ico)
    $bw = New-Object System.IO.BinaryWriter $fs
    # ICONDIR
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
    # ICONDIRENTRY（256x256 は幅/高さ 0 で表す）
    $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngBytes.Length); $bw.Write([UInt32]22)
    $bw.Write($pngBytes)
    $bw.Flush(); $bw.Close(); $fs.Close()
    Write-Host "[OK] アイコン生成: $ico" -ForegroundColor Green
} catch {
    Write-Warning "アイコン生成に失敗しました（アプリexeのアイコンで代替します）: $($_.Exception.Message)"
    if (Test-Path $ico) { Remove-Item $ico -Force }
}

# ── 3) Setup.exe をコンパイル ──────────────────────────────────────
$csc = $null
foreach ($v in @("v4.0.30319", "v3.5", "v2.0.50727")) {
    foreach ($base in @("$env:SystemRoot\Microsoft.NET\Framework64\$v\csc.exe",
                        "$env:SystemRoot\Microsoft.NET\Framework\$v\csc.exe")) {
        if (Test-Path $base) { $csc = $base; break }
    }
    if ($csc) { break }
}
if (-not $csc) { throw "csc.exe（C#コンパイラ）が見つかりません。.NET Framework 4.x を導入してください。" }
Write-Host "== Setup.exe をコンパイル ($csc) ==" -ForegroundColor Cyan

$setupCs  = Join-Path $here "PayCalcDXProSetup.cs"
$setupExe = Join-Path $here "Setup.exe"
$cscArgs  = @("/target:winexe", "/out:$setupExe",
    "/reference:System.Windows.Forms.dll", "/reference:System.Drawing.dll",
    "/reference:Microsoft.CSharp.dll")
if (Test-Path $ico) { $cscArgs += "/win32icon:$ico" }
$cscArgs += $setupCs

& $csc @cscArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $setupExe)) { throw "Setup.exe のコンパイルに失敗しました。" }
Write-Host "[OK] Setup.exe を作成しました。" -ForegroundColor Green

# ── 4) 配布フォルダ installer\ を組み立て ─────────────────────────
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item $setupExe (Join-Path $out "Setup.exe") -Force
Copy-Item (Join-Path $pkg "app")      $out -Recurse -Force
if (Test-Path (Join-Path $pkg "migrator")) { Copy-Item (Join-Path $pkg "migrator") $out -Recurse -Force }
if (Test-Path $ico) { Copy-Item $ico (Join-Path $out "PayCalcDXPro.ico") -Force }
# マニュアル等（PDF）を同梱（Documents\ をそのまま配布物へ）。
$docs = Join-Path $here "Documents"
if (Test-Path $docs) { Copy-Item $docs $out -Recurse -Force }

$readme = @"
================================================================
  給与計算DXPro ブラウザ版 - インストール手順
================================================================

インストール:
  1. Setup.exe をダブルクリックします。
  2. 「インストール」ボタンを押します。
     （管理者権限は不要・.NET のインストールも不要です）
  3. 完了後、デスクトップ／スタートメニューの
     「給与計算DXPro」から起動します。
     起動すると既定ブラウザで画面 (http://localhost:5080) が開きます。

停止:
  ブラウザ（またはタブ）を閉じると、数秒後にサーバが自動で停止します。
  すぐ止めたい場合はスタートメニュー「給与計算DXPro を停止」も使えます。

マニュアル:
  スタートメニューの「はじめての導入ガイド」「操作ガイド」から開けます
  （インストール先の Documents\ にもPDFがあります）。

アンインストール:
  スタートメニュー「アンインストール」、または
  「設定 > アプリ」から「給与計算DXPro」を削除します。
  （給与データは既定で残ります）

同梱物:
  - Setup.exe        インストーラ
  - app\             アプリ本体（self-contained・.NET同梱）
  - migrator\        現行データ移行ツール
  - Documents\       マニュアル（PDF）
  - PayCalcDXPro.ico アイコン

ビルド日時: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
================================================================
"@
Set-Content -Path (Join-Path $out "README.txt") -Value $readme -Encoding UTF8

Write-Host ""
Write-Host "完成: $out" -ForegroundColor Green
Write-Host "配布: このフォルダ一式（またはZip）をユーザーに渡し、Setup.exe を実行してもらう。"
