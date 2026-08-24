# 給与計算DXPro スタンドアロン版 セットアップ

1台のPCにインストールして使う構成です。サーバは不要・**.NETのインストールも不要**（self-contained 発行）。
アプリ起動時にローカルでWebサーバが立ち上がり、既定ブラウザで画面（`http://localhost:5080`）が開きます。

> 📘 画面付き・トラブルシューティング込みの詳細手順は **[セットアップマニュアル.md](セットアップマニュアル.md)** を参照。
> 本ファイルは要点のみのクイックリファレンスです。

## ユーザー向け：インストール手順

1. 配布フォルダ（またはZipを展開したフォルダ）を開く。
2. PowerShell をその場所で開き、次を実行：
   - 現行データを引き継ぐ場合：
     ```powershell
     .\install.ps1 -LegacyDb "現行のPayCalc2026.dbのパス"
     ```
   - 新規（空）でよい場合：
     ```powershell
     .\install.ps1
     ```
   - ※スクリプト実行がブロックされる場合：
     ```powershell
     powershell -ExecutionPolicy Bypass -File .\install.ps1 -LegacyDb "...\PayCalc2026.db"
     ```
3. インストール時に**現行保証の検証**（移行前後でデータ・計算マスタが完全一致するか）が走り、合格しないと中止します。
4. デスクトップの **「給与計算DXPro」** から起動。

### 配置先
- アプリ本体：`<基点フォルダ>\PayCalcDXPro\browser\2026`（年度版。2027年版なら `\2027`）
  - **基点フォルダはセットアップ画面で変更できます**（既定 `C:\syuhousystem`）。`PayCalcDXPro\browser\<年度>` は固定で自動付与されます。
  - 環境により管理者権限が必要な場合あり（求められたら許可）
  - `install.ps1` で入れる場合は `-BaseDir "D:\apps"` のように基点を指定（`-InstallDir` で完全パス指定も可）
- 給与データ：`%LOCALAPPDATA%\PayCalcDXPro\data`（SQLCipher暗号化DB＋DPAPI保護鍵。マイナンバー列はAES-256-GCMで二重暗号化。ユーザー単位で保持）

### アンインストール
```powershell
.\uninstall.ps1            # アプリのみ削除（給与データは保持）
.\uninstall.ps1 -RemoveData  # 給与データも削除（取り消し不可）
```

## 配布担当向け：パッケージ作成

開発機（.NET 8 SDK）で：
```powershell
.\build-package.ps1
```
`package\` に self-contained 発行物（`app\`・`migrator\`）とインストールスクリプトがまとまります。
このフォルダをZip化して配布してください。

### インストーラ(.exe)で配布したい場合【推奨：GUIセットアップ】
```powershell
.\build-setup.ps1
```
`installer\` に **`Setup.exe`＋app\＋migrator\** 一式が作られます。このフォルダ（またはZip）を配布し、
ユーザーは **`Setup.exe` をダブルクリック→「インストール」を押すだけ**です（勤怠管理DXProのSetup.exeを参考）。

- 外部ツール不要：`Setup.exe` は Windows 同梱の C# コンパイラ（csc.exe）で自己ビルドします（Inno Setup 不要）。
- 管理者権限・.NET インストールとも不要（ユーザー領域＝`%LOCALAPPDATA%` に導入、self-contained 同梱）。
- 実施内容：アプリ本体/移行ツールの配置、データフォルダ作成、デスクトップ／スタートメニューのショートカット、
  （任意で）ログオン時自動起動、アイコン付き。停止＝スタートメニュー「給与計算DXPro を停止」、
  アンインストール＝スタートメニュー「アンインストール」または「設定 > アプリ」（給与データは既定で残す）。
- 起動はコンソール非表示（`起動.vbs` 経由）。既定ブラウザで `http://localhost:5080` が自動で開きます。
- **ブラウザ／タブを閉じると数秒後にサーバが自動停止**します（スタンドアロン時のみ）。手動停止＝スタートメニュー「給与計算DXPro を停止」も可。
- **マニュアル（PDF）を同梱**：`deploy\standalone\Documents\*.pdf` を配布物・インストール先の `Documents\` に取り込み、スタートメニューからも開けます。
- `-SkipPublish` を付けると発行を省略し、既存 `package\` を流用して `Setup.exe` だけ作り直せます。

> 素材：`Setup.exe` のアイコンは `wwwroot\img\DXPro2026.png` から自動生成します。
> 固定のアイコンを使いたい場合は `deploy\standalone\PayCalcDXPro.ico` を置いてください（あればそれを使用）。

#### （代替）Inno Setup で作る場合
`setup.iss` を [Inno Setup](https://jrsoftware.org/isinfo.php) でコンパイルしても同等のセットアップexeが作れます
（先に `build-package.ps1` を実行して `package\` を用意）。Inno Setup の導入が必要です。

## 移行ツール（単体実行）
インストール後も移行をやり直せます：
```powershell
%LOCALAPPDATA%\Programs\PayCalcDXPro\migrator\PayCalcDXPro.Migrator.exe `
  --legacy "...\PayCalc2026.db" --out "%LOCALAPPDATA%\PayCalcDXPro\data" --force
```
終了コード 0＝現行保証OK、1＝不一致あり。
