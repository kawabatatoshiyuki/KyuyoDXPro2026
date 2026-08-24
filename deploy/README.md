# オンプレ設置メモ

配置形態は2通り：
- **スタンドアロンPC版（実装済み）**：1台のPCにインストールして使う。サーバ不要・.NET不要。→ [`standalone/`](standalone/README.md)（`build-package.ps1`で発行 → `install.ps1`で導入。現行データ移行＋現行保証検証つき）。
- **サーバ設置型（下記メモ）**：各社1サーバで社内ブラウザから利用。

---

## サーバ設置型（1社1サーバ・1DB）の配置案

## 配置イメージ

- アプリ：self-contained 発行した ASP.NET Core 一式を `C:\PayCalcDXPro\app` に配置
- DB：`C:\PayCalcDXPro\db\PayCalc.db`（SQLCipher で暗号化）
- 鍵：`C:\PayCalcDXPro\keys`（ACL でサービスアカウントのみに限定／Windows DPAPI で保護）
- ログ：`C:\PayCalcDXPro\logs`（アクセス・監査ログ）

## 常駐化

- Kestrel を **Windows サービス**として登録（`sc create` もしくは `New-Service`）
- もしくは IIS をリバースプロキシにして配置
- 利用者は社内PCのブラウザから `https://<サーバ名>/` でアクセス（クライアント側インストール不要）

## セキュリティ

- 全面 HTTPS（社内CA／自己署名）＋ HSTS
- 認証：ASP.NET Core Identity または Active Directory 連携
- RBAC：管理者／給与担当／マイナンバー閲覧 を分離
- マイナンバー：DB全体（SQLCipher）＋列暗号化（AES-256-GCM）の二重防御
- 自動世代バックアップ（暗号化）

## インストーラ

- 現行の vdproj 相当を Inno Setup / MSIX へ置き換え
- 展開→サービス登録→初期DB作成→証明書設定→管理者初期登録 を自動化
