# 給与計算DXPro ブラウザ版（オンプレ）

現行のデスクトップ版「給与計算DXPro」（.NET Framework 4.8 / WinForms / SQLite / VBReport）を、
ブラウザで稼働するオンプレ型システムへ移行するための **再構築ソリューション雛形＋PoC** です。

## 前提・方針（確定済み）

- **配布形態**：各社オンプレ設置型（1社1サーバ・1DB）
- **UI**：Blazor Server（C#のまま画面構築。フロント人材不要）
- **DB**：SQLite継続（1社利用で同時書込が少ないため）＋ **SQLCipher** で透過暗号化
- **ランタイム**：.NET 8 LTS（開発機が .NET 9 のみでも `Directory.Build.props` のロールフォワードで実行可）

## ソリューション構成

```
PayCalcDXPro/
├─ src/
│  ├─ PayCalcDXPro.Domain/      給与・賞与・年調の計算ロジック（UI/DB非依存の純粋ドメイン）★B
│  ├─ PayCalcDXPro.Data/        SQLite(SQLCipher)アクセス・別表/料率の読込（雛形）
│  ├─ PayCalcDXPro.Reporting/   帳票（QuestPDFによるPDF生成）★A
│  ├─ PayCalcDXPro.Security/    マイナンバー列暗号化（AES-256-GCM）・監査（雛形）
│  └─ PayCalcDXPro.Web/         Blazor Server（画面）。計算→PDFダウンロードのデモ実装
├─ tests/
│  └─ PayCalcDXPro.Domain.Tests/  計算ロジック・暗号化の単体テスト（11件）
├─ tools/
│  └─ PayCalcDXPro.PoCRunner/   B→A を一気通貫で実行しPDFを出力するコンソール
└─ deploy/                      オンプレ設置（Windowsサービス化）メモ
```

## PoC で実証したこと

### B. ドメイン抽出 PoC（`PayCalcDXPro.Domain`）
現行 WinForms（`F_給与処理`）のコードビハインドに密結合していた給与計算を、
**UI・DB に依存しない純粋ロジック**として切り出した。

- `SocialInsuranceCalculator`：健保・介護・厚年（労使折半＋50銭ルール）、雇用保険
- `WithholdingTaxCalculator`：源泉所得税「電算機計算の特例」（別表第一〜第四は外部注入）
- `PayrollCalculator`：上記を束ねて 総支給／控除合計／差引支給額 を算出
- xUnit で代表ケースの期待値・不変条件・単調性を検証（合格）

> 別表・料率は `Reference/SampleData.cs`（令和6年分相当の構造）。**本番では現行DBの別表・料率に差し替え、現行システムと計算突合する**こと。端数処理ルールも告示・現行実装と要突合。

### A. 帳票 PoC（`PayCalcDXPro.Reporting`）
VBReport（WinForms専用で載せ替え不可）の置き換え候補として、
**QuestPDF によるサーバ側PDF生成**を検証。`PaySlipPdfGenerator` がドメインの計算結果から
給与明細書PDFをバイト列で生成（Webからはそのままダウンロード応答に流せる）。

> QuestPDF は売上100万USD未満の企業はコミュニティライセンスで無償。日本語は Yu Gothic 等の
> システムフォントで描画。官製様式（源泉徴収票等）の寸法精度は次段で個別検証が必要。

## 実行方法

```powershell
# ビルド
dotnet build PayCalcDXPro.sln

# テスト（11件）
dotnet test PayCalcDXPro.sln

# B→A 一気通貫（PDFを output/ に出力）
dotnet run --project tools/PayCalcDXPro.PoCRunner

# ブラウザ版デモ（計算→PDFダウンロード）
dotnet run --project src/PayCalcDXPro.Web
```

## 手順1〜3の追加成果（現行DB突合・帳票比較・賞与/年調）

### 手順1：現行DBとの突合（`PayCalcDXPro.Data` / `PayCalcDXPro.Integration.Tests`）
現行の暗号化SQLite（AES-128-CBC、鍵 `"SyuhouSystem"`）を `LegacyDbDecryptor` で復号し、
`LegacyMasterRepository` で別表・標準報酬月額表・料率をドメインモデルへ読み出して突合。

突合で判明した**相違をすべて是正**（現行 F_給与処理 のコードに一致）：
1. **社会保険料は料率計算ではなく「標準報酬月額表（T_料率保険料額表）の引き当て」が正**
   → `TableBasedSocialInsuranceCalculator` を追加（健保/介護/厚年は表の確定値）。
2. **別表の境界は「至」が包含的**（例: 課税162,500は第1区分）。サンプルの off-by-one を修正。
3. **給与所得控除（別表1）は `Ceiling(A×率) + 控除額`**（率部は小数点以下切上げ）。
4. **源泉所得税は10円未満四捨五入で固定**。会社別設定 `hasuCode`（四捨五入/切捨/切上）は
   **残業手当計算**用であり所得税には適用しない（当初の推測を訂正）。

### 手順2：官製様式の寸法精度（`docs/帳票方式比較.md`）
源泉徴収票テンプレ（`R_源泉徴収票.xlsx`）で2方式を実機検証。いずれもA4精度を確認。
- **A: xlsxテンプレ流用**（ClosedXMLで流し込み→PDF化）…既存資産・寸法完全一致。サーバは LibreOffice headless 推奨。
- **B: QuestPDFでmm精度描画**（`FormLayoutPdfGenerator`）…外部依存なし・高速。
- 結論＝**ハイブリッド**：官製様式=A、社内帳票（明細・台帳）=B。

### 手順3：賞与・年末調整のドメイン化
- `BonusSocialInsuranceCalculator`（賞与の社会保険料）：標準賞与額×被保険者負担率（50銭処理）、
  健保・介護は年度累計573万円上限、厚年は1回150万円上限。雇用保険も対応。
- `BonusCalculator`（賞与所得税の甲・乙）：T_賞与源泉徴収税額甲/乙から前月給与×扶養で率引き当て。
  前月給与の10倍超／前月給与なしは0（現行準拠）。`源泉区分` で甲乙切替。
- `YearEndAdjustmentCalculator`（給与所得控除後→課税給与所得→算出所得税→年調年税額×102.1%）
  - **4,000円括り**（収入1,900,000〜6,599,999を4,000円単位へ切捨て）実装済み。
  - **所得金額調整控除**（給与収入850万円超・子育て等で (収入−850万)×10%、上限15万）実装済み。
  - 住宅借入金等特別控除（年調所得税額からの控除）に対応。
- **所得控除明細のドメイン化**（`IncomeDeductionCalculator`）：
  - 基礎控除（T_基礎控除表・本人合計所得別）／配偶者（特別）控除（T_配偶者控除・年齢×配偶者所得×本人所得）／
    生命保険料控除（T_生保控除1/2・新旧合算、一般＋介護医療＋個人年金、上限12万）。
  - `YearEndAdjustmentCalculator.CalculateWithDeductions` で**控除明細→年税額まで完結**（合計入力不要）。
- **人的控除のドメイン化**（`DependentDeductionCalculator`、T_扶養障害者等）：
  - 扶養控除（一般38万／特定63万／老人その他48万／同居老親58万／年少は対象外）。
  - 障害者控除（一般27万／特別40万／同居特別75万。本人・配偶者・扶養親族）。
  - 寡婦27万／ひとり親35万（本人所得500万以下）・勤労学生27万（本人所得85万以下）。
  - `人的控除合計(...)` を年末調整の「扶養控除等合計」に投入し、扶養あり年調も完結。
- **生年月日→年齢区分の自動判定**（`AgeClassifier`、現行 ProcessAgeDate/chkNenrei 準拠）：
  - 判定基準「(判定年−年齢)/01/01 以前＝以上、/01/02 以降＝未満」を再現。
  - 老人70+／特定扶養19-22／一般／年少16未満を生年月日から分類。`扶養控除1人(生年月日, 判定年, …)` で直接算定。
- **源泉所得税「乙欄」のドメイン化**（`OtsuWithholdingTaxCalculator`、T_乙税第1〜3表・乙税階差）：
  - 課税対象を5区分（〜10.5万=3.063%／〜74万=階差＋第1/3表の補間／〜171万／171万〜）で算出、扶養1人▲1,610円。
  - `PayrollInput.源泉区分`（甲/乙）で `PayrollCalculator` が甲・乙を自動切替。これで月次所得税が甲乙そろって完成。
- いずれも現行DBの実テーブル／現行ロジックで突合テスト済み。

### 手順4：保存時暗号化への移行（SQLite → SQLCipher）
現行のファイル全体暗号化（AES-128-CBC）から、サーバ常駐に適した SQLCipher の透過暗号化へ移す経路を実証。
- `LegacySqlCipherMigrator`：レガシー復号 → 平文 → `sqlcipher_export` で暗号化DBへ丸ごとコピー。
- `Data` 層を `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher` に変更（鍵未設定なら平文DBも読めるため既存読込と両立）。
- 検証：移行後ファイルは平文ヘッダを持たない／正しい鍵でのみ別表が読める／誤った鍵では `SqliteException`。

### 手順5：マイナンバー列暗号化の本適用（多層防御）
SQLCipher（DB全体暗号化）の上に、マイナンバーの列単位暗号化＋アクセス制御＋監査を実装。
- `MyNumberRepository`（`Data/MyNumber`）：各番号列を `MyNumberProtector`（AES-256-GCM）で**二重に列暗号化**して保存。
- **マスキング表示**（下4桁のみ）は復号権限不要・監査ログなし。
- **全桁の復号**は復号権限が必須で、参照・参照拒否を **`AuditLog`（T_操作ログ）に必ず記録**。
- 検証：保存列は平文を含まない／マスキングは `********9012`／権限ありで全桁取得＋ログ＋1／権限なしは例外＋拒否ログ。
- 本番は SQLCipher 鍵・列暗号化鍵を Windows DPAPI 等で保護する。

### 手順6：認証・認可（RBAC）・鍵管理
ログイン基盤とロールを実装し、マイナンバー復号権限をロールへ結び付け、鍵を DPAPI で保護。
- `PasswordHasher`：PBKDF2-HMAC-SHA256（210,000回・ソルト・固定時間比較）。現行の平文相当パスワードをハッシュ化。
- `AuthenticationService` + `IUserStore` + `UserAccount`：ID/パスワード検証で `Principal` を返す。
- `Principal` / `Role`（管理者・給与担当・マイナンバー閲覧）：`CanDecryptMyNumber` は専用ロールに限定。
  `MyNumberRepository.GetDecrypted` はロールで認可（権限なしは例外＋拒否ログ）。
- `DpapiKeyProtector`：SQLCipher鍵・列暗号鍵を Windows DPAPI で保護してファイル保存／復元／初回生成。
- 検証：ハッシュ往復・誤資格情報の拒否・ロール別復号可否・DPAPI往復（保存値は平文鍵でない）。

> テスト：Domain 32件＋Integration 43件＝**計75件合格**（`dotnet test`）。
> 計算端数・別表は現行 F_給与処理／F_賞与処理／F_年調_従業員情報 のロジックに一致させた。
> 月次（甲乙）・賞与（社保＋甲乙）・年末調整（所得控除明細まで）の計算、帳票方式、保存時暗号化＋
> マイナンバー列暗号化・監査、認証・RBAC・DPAPI鍵保護まで、移行に必要な各PoCが実証済み。

## 次段の課題（本実装に向けて）

1. 特定親族特別控除（tokubetuKoujo）／所得金額調整控除の障害者・23歳未満扶養要件の自動判定
2. 法定調書・各種台帳・源泉徴収簿のドメイン＆帳票化（手順2のハイブリッド方針で）
3. 官製様式のサーバPDF化を LibreOffice headless で再検証＋各欄セル対応付け
4. `IUserStore` の本実装（SQLCipher の M_セキュリティアカウント設定＋ロール列）と Blazor ログイン画面・認可
5. 鍵ローテーション・バックアップ鍵のエスクロー運用設計
6. Windowsサービス化／HTTPS／自動バックアップ／インストーラ（`deploy/`）

> 注1：退職所得の源泉徴収は現行が手入力（自動計算なし）のため、突合対象の計算ロジックは存在しない。
> 注2：現行DB（T_賞与源泉徴収税額乙）の4行目に率の異常値（28798）あり。本番データで要修正。
