using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Bonus;
using PayCalcDXPro.Data.Company;
using PayCalcDXPro.Data.Employee;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Data.MyNumber;
using PayCalcDXPro.Data.PayDay;
using PayCalcDXPro.Data.PayItem;
using PayCalcDXPro.Data.Payroll;
using PayCalcDXPro.Data.Rate;
using PayCalcDXPro.Data.TaxTable;
using PayCalcDXPro.Data.YearEnd;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using PayCalcDXPro.Reporting;
using PayCalcDXPro.Reporting.Models;
using PayCalcDXPro.Security;
using PayCalcDXPro.Web.LegalRecord;
using PayCalcDXPro.Web.Summary;
using PayCalcDXPro.Web.WithholdingSlip;

namespace PayCalcDXPro.Web;

/// <summary>移行済みローカルDB（SQLCipher）の状態。</summary>
public sealed record DbStatus(bool 接続, string 状態, long 従業員数, long テーブル数, long 退職者数 = 0);

/// <summary>ダッシュボード用：支給日情報の登録有無・処理年・処理済みの月（対象労働月）。</summary>
public sealed record PayDayStatus(bool 登録済, int 処理年, IReadOnlyList<int> 処理済月);

/// <summary>支給手当・控除のラベル（名称と、手当の非課税フラグ）。</summary>
public sealed record PayLabel(string 名称, bool 非課税 = false);

/// <summary>支給回（処理月）の選択肢。現行 支給日登録の「計算対象労働月」を表す。</summary>
public sealed record PayRoundOption(int 支給回, int? 対象年, int? 対象月, int? 支給月, bool 登録済)
{
    /// <summary>ドロップダウン表示用ラベル（対象年月が登録済みなら「2026年4月分」、未登録なら「第N回」）。</summary>
    public string ラベル => 登録済 ? $"{対象年}年{対象月}月分" : $"第{支給回}回";
}

/// <summary>賞与回（処理回 1〜4）の選択肢。現行 支給日登録の賞与支給予定を表す。</summary>
public sealed record BonusRoundOption(int 回, int? 支給月, int? 支給日, bool 登録済)
{
    /// <summary>ドロップダウン表示用ラベル（支給予定が登録済みなら「第1回（7/10支給）」、未登録なら「第1回」）。</summary>
    public string ラベル => 登録済 ? $"第{回}回（{支給月}/{支給日}支給）" : $"第{回}回";
}

/// <summary>賞与処理画面の初期データ。</summary>
public sealed record BonusMeta(
    IReadOnlyList<EmployeeListItem> 従業員,
    IReadOnlyList<string> 手当ラベル,
    IReadOnlyList<string> 控除ラベル,
    int 支給年, int? 支給月, int? 支給日,
    IReadOnlyList<BonusRoundOption> 賞与回候補,
    IReadOnlyDictionary<string, string> 固定ラベル,
    IReadOnlyList<int> 保存済従業員);

/// <summary>給与処理画面の初期データ。</summary>
public sealed record PayrollMeta(
    IReadOnlyList<EmployeeListItem> 従業員,
    IReadOnlyList<PayLabel> 手当ラベル,
    IReadOnlyList<string> 控除ラベル,
    string 対象年, string 対象月, string 支給日, int? 所定勤務日数,
    IReadOnlyList<PayRoundOption> 支給回候補,
    IReadOnlyDictionary<string, string> 固定ラベル,
    IReadOnlyList<int> 保存済従業員);

/// <summary>賃金台帳の印刷対象（支給回／賞与回）の選択肢。</summary>
public sealed record WageLedgerRound(string 種別, int 回, string ラベル, bool 選択可, bool 処理済);

/// <summary>賃金台帳 印刷指示画面の選択肢（給与回12・賞与回4・部門・既定選択）。</summary>
public sealed record WageLedgerOptions(
    IReadOnlyList<WageLedgerRound> 給与回,
    IReadOnlyList<WageLedgerRound> 賞与回,
    IReadOnlyList<string> 部門,
    string 既定種別,
    int 既定回);

/// <summary>賃金台帳（従業員別）の従業員選択リストの1件。賃金あり＝当年に給与（賞与）データがある。</summary>
public sealed record WageLedgerEmpSel(
    int Number, string No, string 氏名, string フリガナ, string 部門名, bool 退職, bool 賃金あり);

/// <summary>
/// スタンドアロン版のローカル暗号化DB（<see cref="SecureDataStore"/>）への接続状態を提供する。
/// 単一ユーザーPC想定で DPAPI は CurrentUser。Windows専用。
/// </summary>
public sealed class LocalDatabaseService
{
    public string DataDir { get; }

    public LocalDatabaseService(IConfiguration cfg)
    {
        DataDir = cfg["PayCalc:DataDir"]
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PayCalcDXPro", "data");
    }

    public DbStatus GetStatus()
    {
        if (!OperatingSystem.IsWindows()) return new DbStatus(false, "Windows専用です。", 0, 0);
        if (!SecureDataStore.Exists(DataDir)) return new DbStatus(false, "未移行：データがありません（移行ツールを実行してください）。", 0, 0);

        try
        {
            using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
            long 従業員 = Count(store.Connection, "select count(*) from T_従業員情報");
            long テーブル = Count(store.Connection,
                "select count(*) from sqlite_master where type='table' and name not like 'sqlite_%'");
            // 退職者＝就退区分が3(退職)/4(死亡退職)。
            long 退職 = Count(store.Connection, "select count(*) from T_従業員情報 where 就退区分 in ('3','4')");
            return new DbStatus(true, "接続OK（現行データ移行済み）", 従業員, テーブル, 退職);
        }
        catch (Exception ex)
        {
            AppErrors.Log(ex, "DB接続エラー");
            return new DbStatus(false, "接続エラーが発生しました。詳細はログを確認してください。", 0, 0);
        }
    }

    /// <summary>会社情報を読み込む（未登録なら空の新規インスタンス）。</summary>
    public CompanyInfo LoadCompany()
    {
        if (!OperatingSystem.IsWindows()) return new CompanyInfo();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new CompanyRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load() ?? new CompanyInfo();
    }

    /// <summary>会社情報を保存する。</summary>
    public void SaveCompany(CompanyInfo info)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new CompanyRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(info);
    }

    /// <summary>会社情報と4マスタを1接続でまとめて保存する（画面の一括登録用）。</summary>
    public void SaveAll(CompanyInfo info, CompanyMasters masters)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new CompanyRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(info);
        repo.SaveMasters(masters);
    }

    /// <summary>部門/役職/銀行/報告書提出先の4マスタを読み込む。</summary>
    public CompanyMasters LoadMasters()
    {
        if (!OperatingSystem.IsWindows()) return new CompanyMasters();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new CompanyRepository(store.Connection);
        repo.EnsureSchema();
        return repo.LoadMasters();
    }

    /// <summary>4マスタを保存する。</summary>
    public void SaveMasters(CompanyMasters masters)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new CompanyRepository(store.Connection);
        repo.EnsureSchema();
        repo.SaveMasters(masters);
    }

    /// <summary>コンボ用のコード選択肢を取得する。</summary>
    public IReadOnlyList<CodeOption> GetCodeOptions(string category)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<CodeOption>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new CompanyRepository(store.Connection);
        repo.EnsureSchema();
        return repo.GetCodeOptions(category);
    }

    /// <summary>住民税納付先（報告書提出先 市区町村）の選択肢を取得する。値＝提出先番号、表示＝市区町村名。</summary>
    public IReadOnlyList<CodeOption> GetRecipientOptions()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<CodeOption>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new CompanyRepository(store.Connection);
        repo.EnsureSchema();
        return repo.GetRecipientOptions();
    }

    // ===== 従業員（Phase1：基本情報） =====

    /// <summary>従業員一覧を取得する。</summary>
    public IReadOnlyList<EmployeeListItem> ListEmployees()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<EmployeeListItem>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        // 一覧は部門/役職マスタとJOINするため両スキーマを用意する。
        new CompanyRepository(store.Connection).EnsureSchema();
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        return repo.List();
    }

    /// <summary>従業員を1件読み込む（無ければ null）。</summary>
    public Employee? LoadEmployee(int number)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(number);
    }

    // ===== マイナンバー登録 =====

    private MyNumberProtector? _mnp;

    /// <summary>マイナンバー列暗号鍵（DPAPIで保護した256bit鍵）。鍵ファイルが無ければ生成する。</summary>
    private MyNumberProtector GetMyNumberProtector()
    {
        if (_mnp is null)
        {
            byte[] key = DpapiKeyProtector.LoadOrCreate(
                Path.Combine(DataDir, "keys", "mynumber.key"), 32, DataProtectionScope.CurrentUser);
            _mnp = new MyNumberProtector(key);
        }
        return _mnp;
    }

    // 監査ログ・マイナンバー操作の操作者。移植元 AccountInfo（グローバル単一のログイン利用者）と同じく、
    // 単一ユーザーPC運用のためログイン中の実ユーザーを1つ保持する。未ログイン（セキュリティアカウント未設定）時は
    // システム（管理者）で記録する（現行の固定挙動を踏襲）。Program.cs のログイン/ログアウト・認証ミドルウェアで設定する。
    private static readonly Principal SystemOperator = Principal.Of("system", "システム", Role.管理者);
    private volatile Principal? _currentOperator;

    /// <summary>現在ログイン中の操作者（未ログイン時は null＝システム扱い）。Program.cs のログイン/ログアウトで設定する。</summary>
    public Principal? CurrentOperator { get => _currentOperator; set => _currentOperator = value; }

    /// <summary>監査ログ・マイナンバー操作に使う操作者。ログイン中は実ユーザー、未ログインはシステム（管理者）。</summary>
    private Principal 操作者 => _currentOperator ?? SystemOperator;

    /// <summary>マイナンバー登録画面の表示モデル（本人＋配偶者＋扶養親族7人の氏名・登録可否・値）を取得する。
    /// 氏名は本人＝従業員情報、配偶者・扶養親族＝年末調整の登録名から取り、年末調整未登録の枠は入力不可とする。</summary>
    public MyNumberView LoadMyNumber(int number)
    {
        if (!OperatingSystem.IsWindows()) return new MyNumberView(number, "", "", Array.Empty<MyNumberSlot>());
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var emp = ListEmployeesInternal(conn).FirstOrDefault(x => x.Number == number);
        var ye = new YearEndRepository(conn);
        ye.EnsureSchema();
        var yeEntry = ye.Load(number);

        var repo = new MyNumberRepository(conn, GetMyNumberProtector(), new AuditLog(conn));
        repo.EnsureSchema();
        MyNumberRecord? rec = null;
        try { rec = repo.GetDecrypted(number.ToString(), 操作者); }
        catch (KeyNotFoundException) { /* 未登録＝空欄 */ }

        var 名前 = new string[MyNumberSlots.Count];
        名前[0] = emp?.氏名 ?? "";
        名前[1] = yeEntry?.配偶者.氏名 ?? "";
        for (int i = 0; i < 7; i++) 名前[2 + i] = yeEntry?.扶養親族[i].氏名 ?? "";

        var 値 = new string[MyNumberSlots.Count];
        値[0] = rec?.本人 ?? "";
        値[1] = rec?.配偶者 ?? "";
        for (int i = 0; i < 7; i++) 値[2 + i] = (rec is not null && i < rec.扶養親族.Count ? rec.扶養親族[i] : null) ?? "";

        var slots = new List<MyNumberSlot>(MyNumberSlots.Count);
        for (int i = 0; i < MyNumberSlots.Count; i++)
        {
            bool 登録 = i == 0 || !string.IsNullOrWhiteSpace(名前[i]);   // 本人は常に入力可
            slots.Add(new MyNumberSlot(i, MyNumberSlots.種別[i], 名前[i], 登録, 値[i]));
        }
        return new MyNumberView(number, emp?.No ?? "", emp?.氏名 ?? "", slots);
    }

    /// <summary>マイナンバーを検証して保存する。形式不正があれば保存せず、不正スロットのインデックス一覧を返す（空＝保存成功）。</summary>
    public IReadOnlyList<int> SaveMyNumber(int number, IReadOnlyList<string> values)
    {
        var invalid = new List<int>();
        for (int i = 0; i < values.Count && i < MyNumberSlots.Count; i++)
            if (!MyNumberValidator.IsValid(values[i])) invalid.Add(i);
        if (invalid.Count > 0 || !OperatingSystem.IsWindows()) return invalid;

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new MyNumberRepository(conn, GetMyNumberProtector(), new AuditLog(conn));
        repo.EnsureSchema();

        static string? Empty(string s) => string.IsNullOrEmpty(s) ? null : s;
        var rec = new MyNumberRecord
        {
            Number = number.ToString(),
            本人 = Empty(values[0]),
            配偶者 = Empty(values[1]),
            扶養親族 = Enumerable.Range(2, 7).Select(i => Empty(values[i])).ToArray(),
        };
        repo.Save(rec, 操作者);
        return invalid;   // 空＝成功
    }

    private static IReadOnlyList<EmployeeListItem> ListEmployeesInternal(SqliteConnection conn)
    {
        new CompanyRepository(conn).EnsureSchema();
        var repo = new EmployeeRepository(conn);
        repo.EnsureSchema();
        return repo.List();
    }

    // ===== アクセスログ（操作ログ）参照・出力 =====

    /// <summary>操作ログを新しい順に取得する（行・総件数）。</summary>
    public (IReadOnlyList<AuditLogRow> 行, long 件数) LoadAuditLogs(int limit = 5000)
    {
        if (!OperatingSystem.IsWindows()) return (Array.Empty<AuditLogRow>(), 0);
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var audit = new AuditLog(store.Connection);
        audit.EnsureSchema();
        return (audit.List(limit), audit.Count());
    }

    /// <summary>操作ログを CSV（UTF-8 BOM・全列ダブルクォート）に出力する。出力操作自体もログに記録する（現行踏襲）。</summary>
    public byte[] BuildAuditLogCsv()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var audit = new AuditLog(store.Connection);
        audit.EnsureSchema();

        var rows = audit.List(int.MaxValue);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\"日時\",\"アカウントID\",\"操作者\",\"処理\",\"取扱\",\"詳細\"");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                new[] { r.日時, r.アカウントID, r.操作者, r.処理, r.取扱, r.詳細 }.Select(Quote)));

        // 現行 F_アクセスログ参照出力：出力完了をログ登録。
        audit.Put(操作者, "アクセスログ参照・出力", "出力", "CSV出力");

        return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());

        static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }

    // ===== 会社情報・従業員情報の CSV エクスポート =====

    /// <summary>CSVの1セルを "" で囲みエスケープする（"→""）。</summary>
    private static string CsvCell(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    /// <summary>Excelで文字化けしないよう UTF-8 BOM 付きバイト列にする（GetBytesはBOMを付けないため先頭に付与）。</summary>
    private static byte[] Utf8Bom(string text)
    {
        var body = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        var result = new byte[3 + body.Length];
        result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
        Buffer.BlockCopy(body, 0, result, 3, body.Length);
        return result;
    }

    /// <summary>会社の基本情報を CSV（1行目＝見出し、2行目＝データ）で出力する。UTF-8 BOM 付き。</summary>
    public byte[] BuildCompanyCsv()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new CompanyRepository(store.Connection);
        repo.EnsureSchema();
        var c = repo.Load() ?? new CompanyInfo();

        // コード値は読みやすいラベルへ変換する（見つからなければコードのまま）。
        string 区分(string code, string category)
            => repo.GetCodeOptions(category).FirstOrDefault(o => o.コード == code)?.値 ?? code;

        var rows = new (string 項目, string 値)[]
        {
            ("会社名", c.会社名),
            ("会社名カナ", c.会社名カナ),
            ("事業所名", c.事業所名),
            ("事業種目", c.事業種目),
            ("郵便番号", c.郵便番号),
            ("所在地", c.所在地),
            ("所在地カナ", c.所在地カナ),
            ("電話番号", c.ＴＥＬ),
            ("代表者名", c.代表者名),
            ("代表者名カナ", c.代表者氏名カナ),
            ("連絡者名", c.連絡者名),
            ("連絡者名カナ", c.連絡者名カナ),
            ("番号区分", c.番号区分 == "2" ? "個人事業主" : "法人"),
            ("法人番号", c.番号区分 == "1" ? c.事業所マイナンバー : ""),
            ("所轄税務署名", c.税務署名),
            ("署番号", c.署番号),
            ("整理番号", c.整理番号),
            ("支払方法", c.支払方法),
            ("支払金融機関", c.支払金融機関),
            ("金融機関所在地", c.金融機関所在地),
            ("労働保険適用事業所", 区分(c.労働保険適用事業所, "020")),
            ("年末調整方法", 区分(c.年末調整方法, "021")),
            ("残業手当計算の端数処理", 区分(c.残業手当計算端数処理, "022")),
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", rows.Select(r => CsvCell(r.項目))));   // 1行目：見出し
        sb.AppendLine(string.Join(",", rows.Select(r => CsvCell(r.値))));     // 2行目：データ
        return Utf8Bom(sb.ToString());
    }

    /// <summary>
    /// 選択した従業員の基本情報を CSV（1行1名）で出力する。<paramref name="numbers"/> が null/空なら全従業員。
    /// 出力順は従業員一覧の並び順。コード値はラベルに変換する。UTF-8 BOM 付き。個人情報のため監査ログに残す。
    /// </summary>
    public byte[] BuildEmployeesCsv(IEnumerable<int>? numbers)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var company = new CompanyRepository(store.Connection);
        company.EnsureSchema();
        var empRepo = new EmployeeRepository(store.Connection);
        empRepo.EnsureSchema();

        // コード→ラベルの辞書（部門/役職/銀行/性別/就退区分/所得税源泉）。
        static Dictionary<string, string> ToMap(IReadOnlyList<CodeOption> list)
            => list.ToDictionary(o => o.コード, o => o.値);
        var 部門 = ToMap(company.GetDepartmentOptions());
        var 役職 = ToMap(company.GetPositionOptions());
        var 銀行 = ToMap(company.GetBankOptions());
        var 性別 = ToMap(company.GetCodeOptions("017"));
        var 就退 = ToMap(company.GetCodeOptions("014"));
        var 源泉 = ToMap(company.GetCodeOptions("010"));
        static string L(Dictionary<string, string> m, string code) => m.TryGetValue(code ?? "", out var v) ? v : "";
        static string 預金(string c) => c == "2" ? "当座" : "普通";

        // 出力対象＝指定番号（一覧の並び順を維持）。未指定なら全員。
        var order = empRepo.List().Select(e => e.Number).ToList();
        var want = numbers?.ToHashSet();
        var targets = (want is null || want.Count == 0) ? order : order.Where(want.Contains).ToList();

        var header = new[] { "No", "氏名", "フリガナ", "性別", "生年月日", "郵便番号", "住所1", "住所2",
            "部門", "役職", "就退区分", "就退年月日", "振込銀行", "預金種類", "口座番号", "所得税源泉区分" };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", header.Select(CsvCell)));

        int count = 0;
        foreach (var num in targets)
        {
            var e = empRepo.Load(num);
            if (e is null) continue;
            var cells = new[]
            {
                e.No, e.氏名, e.フリガナ, L(性別, e.性別コード), e.生年月日, e.郵便番号,
                e.住所１, e.住所２, L(部門, e.部門番号), L(役職, e.役職番号),
                L(就退, e.就退区分), e.就退年月日, L(銀行, e.銀行番号), 預金(e.預金種類),
                e.口座番号, L(源泉, e.所得税源泉),
            };
            sb.AppendLine(string.Join(",", cells.Select(CsvCell)));
            count++;
        }

        // 個人情報の持ち出しは証跡として監査ログに残す。
        try
        {
            var audit = new AuditLog(store.Connection);
            audit.EnsureSchema();
            audit.Put(操作者, "従業員情報CSV出力", "出力", $"{count}名");
        }
        catch { /* ログ失敗は出力自体を止めない */ }

        return Utf8Bom(sb.ToString());
    }

    // ===== 退職所得の源泉徴収票・特別徴収票（現行 F_年調_退職源泉） =====

    private const string 退職源泉処理名 = "退職所得の源泉徴収票・特別徴収票";

    /// <summary>登録済みの退職源泉票を整理番号順に取得する。</summary>
    public IReadOnlyList<RetirementWithholdingListItem> ListRetirementWithholdings()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<RetirementWithholdingListItem>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RetirementWithholdingRepository(store.Connection);
        repo.EnsureSchema();
        return repo.List();
    }

    /// <summary>退職源泉票を1件読み込む（無ければ null）。</summary>
    public RetirementWithholdingRecord? LoadRetirementWithholding(int no)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RetirementWithholdingRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(no);
    }

    /// <summary>退職源泉票を保存する（No=0 は新規登録＝採番、それ以外は更新）。採番/更新後の No を返す。</summary>
    public int SaveRetirementWithholding(RetirementWithholdingRecord e)
    {
        if (!OperatingSystem.IsWindows()) return e.No;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RetirementWithholdingRepository(conn);
        repo.EnsureSchema();

        int no;
        if (e.No <= 0) { no = repo.Insert(e); }
        else { repo.Update(e); no = e.No; }

        new AuditLog(conn).Put(操作者, 退職源泉処理名, "更新", $"整理番号:{no} 個人番号:{e.個人番号}");
        return no;
    }

    /// <summary>退職源泉票を削除する。</summary>
    public void DeleteRetirementWithholding(int no, string 個人番号)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RetirementWithholdingRepository(conn);
        repo.EnsureSchema();
        repo.Delete(no);
        new AuditLog(conn).Put(操作者, 退職源泉処理名, "削除", $"整理番号:{no} 個人番号:{個人番号}");
    }

    // ---- 報酬、料金、契約金及び賞金の支払調書（現行 T_年調報酬情報 / F_年調_報酬）----
    private const string 報酬処理名 = "報酬、料金、契約金及び賞金の支払調書";

    /// <summary>報酬支払調書の一覧（整理番号順・名称つき）。</summary>
    public IReadOnlyList<RewardListItem> ListRewards()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<RewardListItem>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RewardRepository(store.Connection);
        repo.EnsureSchema();
        return repo.List();
    }

    /// <summary>報酬支払調書を1件読み込む（無ければ null）。</summary>
    public RewardRecord? LoadReward(int no)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RewardRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(no);
    }

    /// <summary>報酬支払調書を保存する（No=0 は新規登録＝採番、それ以外は更新）。採番/更新後の No を返す。</summary>
    public int SaveReward(RewardRecord e)
    {
        if (!OperatingSystem.IsWindows()) return e.No;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RewardRepository(conn);
        repo.EnsureSchema();

        int no;
        if (e.No <= 0) { no = repo.Insert(e); }
        else { repo.Update(e); no = e.No; }

        new AuditLog(conn).Put(操作者, 報酬処理名, "更新", $"整理番号:{no} 番号:{e.支払受者番号}");
        return no;
    }

    /// <summary>報酬の個人番号（マイナンバー）参照をアクセスログに記録する（現行 F_報酬支払受者番号の入力 の"参照"相当）。</summary>
    public void LogRewardMyNumberView(string 番号)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        new AuditLog(store.Connection).Put(操作者, "報酬料金契約金及び賞金（支払受者番号の入力）", "参照",
            $"参照した番号:{番号}");
    }

    /// <summary>報酬支払調書を削除する。</summary>
    public void DeleteReward(int no, string 番号)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RewardRepository(conn);
        repo.EnsureSchema();
        repo.Delete(no);
        new AuditLog(conn).Put(操作者, 報酬処理名, "削除", $"整理番号:{no} 番号:{番号}");
    }

    // ---- 不動産の使用料等の支払調書（現行 T_不動産情報 / F_年調_不動産）----
    private const string 不動産処理名 = "不動産の使用料等の支払調書";

    /// <summary>不動産支払調書の一覧（整理番号順・名称つき）。</summary>
    public IReadOnlyList<RealEstateListItem> ListRealEstates()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<RealEstateListItem>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateRepository(store.Connection);
        repo.EnsureSchema();
        return repo.List();
    }

    /// <summary>不動産支払調書を1件読み込む（無ければ null）。</summary>
    public RealEstateRecord? LoadRealEstate(int no)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(no);
    }

    /// <summary>不動産支払調書を保存する（No=0 は新規登録＝採番、それ以外は更新）。採番/更新後の No を返す。</summary>
    public int SaveRealEstate(RealEstateRecord e)
    {
        if (!OperatingSystem.IsWindows()) return e.No;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RealEstateRepository(conn);
        repo.EnsureSchema();

        int no;
        if (e.No <= 0) { no = repo.Insert(e); }
        else { repo.Update(e); no = e.No; }

        new AuditLog(conn).Put(操作者, 不動産処理名, "更新",
            $"整理番号:{no} 受給者番号:{e.支払受者番号} あっせん者番号:{e.あっせん者番号}");
        return no;
    }

    /// <summary>不動産の個人番号（マイナンバー）参照をアクセスログに記録する（現行 F_不動産支払受者番号/あっせん者番号 の"参照"相当）。</summary>
    public void LogRealEstateMyNumberView(string 番号, bool あっせん)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        string 処理 = あっせん ? "不動産の使用料等（あっせん者番号の入力）" : "不動産の使用料等（支払受者番号の入力）";
        new AuditLog(store.Connection).Put(操作者, 処理, "参照", $"参照した番号:{番号}");
    }

    /// <summary>不動産支払調書を削除する。</summary>
    public void DeleteRealEstate(int no, string 支払受者番号, string あっせん者番号)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RealEstateRepository(conn);
        repo.EnsureSchema();
        repo.Delete(no);
        new AuditLog(conn).Put(操作者, 不動産処理名, "削除",
            $"整理番号:{no} 受給者番号:{支払受者番号} あっせん者番号:{あっせん者番号}");
    }

    // ---- 不動産等の譲受けの対価の支払調書（現行 T_不動産譲受 / F_年調_不動産譲受）----
    private const string 不動産譲受処理名 = "不動産等の譲受けの対価の支払調書";

    /// <summary>不動産譲受支払調書の一覧（整理番号順・名称つき）。</summary>
    public IReadOnlyList<RealEstateTransferListItem> ListRealEstateTransfers()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<RealEstateTransferListItem>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateTransferRepository(store.Connection);
        repo.EnsureSchema();
        return repo.List();
    }

    /// <summary>不動産譲受支払調書を1件読み込む（無ければ null）。</summary>
    public RealEstateTransferRecord? LoadRealEstateTransfer(int no)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateTransferRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(no);
    }

    /// <summary>不動産譲受支払調書を保存する（No=0 は新規登録＝採番、それ以外は更新）。採番/更新後の No を返す。</summary>
    public int SaveRealEstateTransfer(RealEstateTransferRecord e)
    {
        if (!OperatingSystem.IsWindows()) return e.No;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RealEstateTransferRepository(conn);
        repo.EnsureSchema();

        int no;
        if (e.No <= 0) { no = repo.Insert(e); }
        else { repo.Update(e); no = e.No; }

        new AuditLog(conn).Put(操作者, 不動産譲受処理名, "更新",
            $"整理番号:{no} 受給者番号:{e.支払受者番号} あっせん者番号:{e.あっせん者番号}");
        return no;
    }

    /// <summary>不動産譲受の個人番号（マイナンバー）参照をアクセスログに記録する。</summary>
    public void LogRealEstateTransferMyNumberView(string 番号, bool あっせん)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        string 処理 = あっせん ? "不動産等の譲受けの対価（あっせん者番号の入力）" : "不動産等の譲受けの対価（支払受者番号の入力）";
        new AuditLog(store.Connection).Put(操作者, 処理, "参照", $"参照した番号:{番号}");
    }

    /// <summary>不動産譲受支払調書を削除する。</summary>
    public void DeleteRealEstateTransfer(int no, string 支払受者番号, string あっせん者番号)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RealEstateTransferRepository(conn);
        repo.EnsureSchema();
        repo.Delete(no);
        new AuditLog(conn).Put(操作者, 不動産譲受処理名, "削除",
            $"整理番号:{no} 受給者番号:{支払受者番号} あっせん者番号:{あっせん者番号}");
    }

    // ---- 不動産等の売買又は貸付けのあっせん手数料の支払調書（現行 T_不動産あっせん情報 / F_年調_不動産あっせん）----
    private const string 不動産あっせん処理名 = "不動産等の売買又は貸付けのあっせん手数料の支払調書";

    /// <summary>不動産あっせん支払調書の一覧（整理番号順・名称つき）。</summary>
    public IReadOnlyList<RealEstateMediationListItem> ListRealEstateMediations()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<RealEstateMediationListItem>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateMediationRepository(store.Connection);
        repo.EnsureSchema();
        return repo.List();
    }

    /// <summary>不動産あっせん支払調書を1件読み込む（無ければ null）。</summary>
    public RealEstateMediationRecord? LoadRealEstateMediation(int no)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateMediationRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(no);
    }

    /// <summary>不動産あっせん支払調書を保存する（No=0 は新規登録＝採番、それ以外は更新）。採番/更新後の No を返す。</summary>
    public int SaveRealEstateMediation(RealEstateMediationRecord e)
    {
        if (!OperatingSystem.IsWindows()) return e.No;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RealEstateMediationRepository(conn);
        repo.EnsureSchema();

        int no;
        if (e.No <= 0) { no = repo.Insert(e); }
        else { repo.Update(e); no = e.No; }

        new AuditLog(conn).Put(操作者, 不動産あっせん処理名, "更新",
            $"整理番号:{no} 受給者番号:{e.支払受者番号}");
        return no;
    }

    /// <summary>不動産あっせんの個人番号（マイナンバー）参照をアクセスログに記録する。</summary>
    public void LogRealEstateMediationMyNumberView(string 番号)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        new AuditLog(store.Connection).Put(操作者,
            "不動産等の売買又は貸付けのあっせん料（支払受者番号の入力）", "参照", $"参照した番号:{番号}");
    }

    /// <summary>不動産あっせん支払調書を削除する。</summary>
    public void DeleteRealEstateMediation(int no, string 支払受者番号)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;
        var repo = new RealEstateMediationRepository(conn);
        repo.EnsureSchema();
        repo.Delete(no);
        new AuditLog(conn).Put(操作者, 不動産あっせん処理名, "削除",
            $"整理番号:{no} 受給者番号:{支払受者番号}");
    }

    /// <summary>
    /// 不動産の使用料等の支払調書（現行 R_不動産）を1件分PDF化する（4面付け1ページ）。
    /// 現行 createReport のセル代入を忠実に移植。区分N は 支払金額N がある行のみ描画（現行の分岐）。
    /// 会社情報・支給年もあわせて描画する。無ければ空。
    /// </summary>
    public byte[] BuildRealEstatePdf(int no)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var repo = new RealEstateRepository(conn);
        repo.EnsureSchema();
        RealEstateRecord? e = repo.Load(no);
        if (e is null) return Array.Empty<byte>();

        var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
        var payday = new PayDayRepository(conn);
        payday.EnsureSchema();
        int 支給年 = payday.Load().支給年;

        // 区分N は 支払金額N がある行のみ（現行 createReport は金額の notEntered 判定内で区分・金額をセット）。
        var 区分 = new string[5];
        for (int i = 0; i < 5; i++) 区分[i] = e.支払金額[i].HasValue ? e.区分[i] : "";

        var data = new RealEstateReportData
        {
            支給年 = 支給年,
            所在地 = e.所在地, 名称 = e.名称,
            支払受者区分 = e.支払受者区分, 支払受者番号 = e.支払受者番号,
            区分 = 区分,
            物件の所在地 = (string[])e.物件の所在地.Clone(),
            細目 = (string[])e.細目.Clone(),
            計算の基礎 = (string[])e.計算の基礎.Clone(),
            支払金額 = (int?[])e.支払金額.Clone(),
            摘要 = e.摘要,
            あっせん所在地 = e.あっせん所在地, あっせん名称 = e.あっせん名称,
            あっせん者区分 = e.あっせん者区分, あっせん者番号 = e.あっせん者番号,
            支払確定年月日 = e.支払確定年月日, あっせん手数料 = e.あっせん手数料,
            会社所在地 = company.所在地, 会社名 = company.会社名, 会社電話 = company.ＴＥＬ,
            事業所マイナンバー = company.事業所マイナンバー, 会社番号区分 = company.番号区分,
        };

        return new RealEstateReport().CreatePdf(data);
    }

    /// <summary>
    /// 不動産等の譲受けの対価の支払調書（現行 R_不動産譲受）を1件分PDF化する（4面付け1ページ）。
    /// 現行 createReport のセル代入を忠実に移植。物件の種類N・支払金額N は 支払金額N がある行のみ描画（generator側で分岐）。
    /// 会社情報・支給年もあわせて描画する。無ければ空。
    /// </summary>
    public byte[] BuildRealEstateTransferPdf(int no)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var repo = new RealEstateTransferRepository(conn);
        repo.EnsureSchema();
        RealEstateTransferRecord? e = repo.Load(no);
        if (e is null) return Array.Empty<byte>();

        var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
        var payday = new PayDayRepository(conn);
        payday.EnsureSchema();
        int 支給年 = payday.Load().支給年;

        var data = new RealEstateTransferReportData
        {
            支給年 = 支給年,
            所在地 = e.所在地, 名称 = e.名称,
            支払受者区分 = e.支払受者区分, 支払受者番号 = e.支払受者番号,
            物件の種類 = (string[])e.物件の種類.Clone(),
            物件の所在地 = (string[])e.物件の所在地.Clone(),
            細目 = (string[])e.細目.Clone(),
            数量 = (string[])e.数量.Clone(),
            取得年月日 = (DateTime?[])e.取得年月日.Clone(),
            支払金額 = (int?[])e.支払金額.Clone(),
            摘要1 = e.摘要[0], 摘要2 = e.摘要[1],
            あっせん所在地 = e.あっせん所在地, あっせん名称 = e.あっせん名称,
            あっせん者区分 = e.あっせん者区分, あっせん者番号 = e.あっせん者番号,
            支払確定年月日 = e.支払確定年月日, あっせん手数料 = e.あっせん手数料,
            会社所在地 = company.所在地, 会社名 = company.会社名, 会社電話 = company.ＴＥＬ,
            事業所マイナンバー = company.事業所マイナンバー, 会社番号区分 = company.番号区分,
        };

        return new RealEstateTransferReport().CreatePdf(data);
    }

    /// <summary>
    /// 不動産等の売買又は貸付けのあっせん手数料の支払調書（現行 R_不動産あっせん）を1件分PDF化する（4面付け1ページ）。
    /// 現行 createReport のセル代入を忠実に移植（明細・金額は無条件代入・金額は空/0で空欄）。
    /// 表示ラベルとDB列名の対応：支払確定年月日＝DB「取得年月日」／取引金額＝DB「取得金額」。
    /// 会社情報・支給年もあわせて描画する。無ければ空。
    /// </summary>
    public byte[] BuildRealEstateMediationPdf(int no)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var repo = new RealEstateMediationRepository(conn);
        repo.EnsureSchema();
        RealEstateMediationRecord? e = repo.Load(no);
        if (e is null) return Array.Empty<byte>();

        var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
        var payday = new PayDayRepository(conn);
        payday.EnsureSchema();
        int 支給年 = payday.Load().支給年;

        var data = new RealEstateMediationReportData
        {
            支給年 = 支給年,
            所在地 = e.所在地, 名称 = e.名称,
            支払受者区分 = e.支払受者区分, 支払受者番号 = e.支払受者番号,
            区分 = (string[])e.区分.Clone(),
            支払確定年月日 = (DateTime?[])e.取得年月日.Clone(),   // DB「取得年月日」→表示「支払確定年月日」
            支払金額 = (int?[])e.支払金額.Clone(),
            物件の種類 = (string[])e.物件の種類区分.Clone(),
            物件の所在地 = (string[])e.物件の所在地.Clone(),
            数量 = (string[])e.数量.Clone(),
            取引金額 = (int?[])e.取得金額.Clone(),               // DB「取得金額」→表示「取引金額」
            摘要 = (string[])e.摘要.Clone(),
            会社所在地 = company.所在地, 会社名 = company.会社名, 会社電話 = company.ＴＥＬ,
            事業所マイナンバー = company.事業所マイナンバー, 会社番号区分 = company.番号区分,
        };

        return new RealEstateMediationReport().CreatePdf(data);
    }

    /// <summary>
    /// 報酬、料金、契約金及び賞金の支払調書（現行 R_報酬）を1件分PDF化する（4面付け1ページ）。
    /// 現行 createReport のセル代入を忠実に移植。1区分目は「区分名称 or 年調報酬区分コード名」で解決し、
    /// 区分2〜5は生値。会社情報・支給年もあわせて描画する。無ければ空配列。
    /// </summary>
    public byte[] BuildRewardPdf(int no)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var repo = new RewardRepository(conn);
        repo.EnsureSchema();
        RewardRecord? e = repo.Load(no);
        if (e is null) return Array.Empty<byte>();

        var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
        var payday = new PayDayRepository(conn);
        payday.EnsureSchema();
        int 支給年 = payday.Load().支給年;

        // 区分1＝区分名称があれば優先、無ければ年調報酬区分コード名。区分2〜5は生値（現行 createReport）。
        var 区分 = new string[5];
        区分[0] = !string.IsNullOrEmpty(e.区分名称)
            ? e.区分名称
            : RewardRepository.Kubuns.FirstOrDefault(k => k.コード == e.区分[0])?.名称 ?? "";
        for (int j = 1; j < 5; j++) 区分[j] = e.区分[j];

        var data = new RewardReportData
        {
            支給年 = 支給年,
            所在地 = e.所在地, 名称 = e.名称,
            支払受者区分 = e.支払受者区分, 支払受者番号 = e.支払受者番号,
            区分 = 区分,
            細目 = (string[])e.細目.Clone(),
            支払金額 = (int?[])e.支払金額.Clone(),
            内未払 = (int?[])e.内未払.Clone(),
            源泉徴収税額 = (int?[])e.源泉徴収税額.Clone(),
            内未徴収税額 = (int?[])e.内未徴収税額.Clone(),
            摘要 = e.摘要,
            会社所在地 = company.所在地, 会社名 = company.会社名, 会社電話 = company.ＴＥＬ,
            事業所マイナンバー = company.事業所マイナンバー, 会社番号区分 = company.番号区分,
        };

        return new RewardReport().CreatePdf(data);
    }

    /// <summary>
    /// 退職所得の源泉徴収票・特別徴収票（現行 R_退職源泉）を1件分PDF化する（4面付け1ページ）。
    /// 現行 createReport のセル代入を忠実に移植。会社情報・支給年もあわせて描画する。無ければ空配列。
    /// </summary>
    public byte[] BuildRetirementWithholdingPdf(int no)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var repo = new RetirementWithholdingRepository(conn);
        repo.EnsureSchema();
        RetirementWithholdingRecord? e = repo.Load(no);
        if (e is null) return Array.Empty<byte>();

        var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
        var payday = new PayDayRepository(conn);
        payday.EnsureSchema();
        int 支給年 = payday.Load().支給年;

        var data = new RetirementWithholdingReportData
        {
            支給年 = 支給年,
            氏名 = e.氏名, 個人番号 = e.個人番号, 住所 = e.住所, 年始住所 = e.年始住所,
            支払金額 = (int[])e.支払金額.Clone(),
            源泉徴収税額 = (int[])e.源泉徴収税額.Clone(),
            市町村民税 = (int[])e.市町村民税.Clone(),
            道府県民税 = (int[])e.道府県民税.Clone(),
            退職所得控除額 = e.退職所得控除額,
            勤続年数 = e.勤続年数, 就職年月日 = e.就職年月日, 退職年月日 = e.退職年月日, 摘要 = e.摘要,
            会社所在地 = company.所在地, 会社名 = company.会社名, 会社電話 = company.ＴＥＬ,
            番号区分 = company.番号区分, 事業所マイナンバー = company.事業所マイナンバー,
        };

        return new RetirementWithholdingReport().CreatePdf(data);
    }

    /// <summary>退職者選択の候補（中途退職・死亡退職＝就退区分3/4）を取得する。</summary>
    public IReadOnlyList<RetiredEmployeeItem> ListRetiredEmployees()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<RetiredEmployeeItem>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        return ListEmployeesInternal(store.Connection)
            .Where(x => x.退職)
            .Select(x => new RetiredEmployeeItem(
                x.Number, x.No,
                string.IsNullOrWhiteSpace(x.役職名) ? x.氏名 : $"{x.役職名}　{x.氏名}"))
            .ToList();
    }

    /// <summary>退職者選択：従業員の役職氏名・住所・退職年月日と、マイナンバー本人（登録済みなら）を取り込む。</summary>
    public RetiredEmployeePrefill PrefillRetiredEmployee(int number)
    {
        if (!OperatingSystem.IsWindows()) return new RetiredEmployeePrefill("", "", "", "");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var emp = new EmployeeRepository(conn).Load(number);
        if (emp is null) return new RetiredEmployeePrefill("", "", "", "");

        string 役職名 = ListEmployeesInternal(conn).FirstOrDefault(x => x.Number == number)?.役職名 ?? "";
        string 役職氏名 = emp.役職番号 != "0" && !string.IsNullOrWhiteSpace(役職名)
            ? $"{役職名}　{emp.氏名}" : emp.氏名;
        string 住所 = string.Join("　", new[] { emp.住所１, emp.住所２ }.Where(s => !string.IsNullOrWhiteSpace(s)));

        string 個人番号 = "";
        try
        {
            var mn = new MyNumberRepository(conn, GetMyNumberProtector(), new AuditLog(conn));
            mn.EnsureSchema();
            個人番号 = mn.GetDecrypted(number.ToString(), 操作者).本人 ?? "";
        }
        catch (KeyNotFoundException) { /* マイナンバー未登録 */ }

        return new RetiredEmployeePrefill(役職氏名, 個人番号, 住所, emp.就退年月日);
    }

    /// <summary>従業員を保存する。採番したNumberを返す。</summary>
    public int SaveEmployee(Employee employee)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Save(employee);
    }

    /// <summary>一覧のドラッグ並び替え結果を保存する。</summary>
    public void UpdateEmployeeOrder(IReadOnlyList<int> orderedNumbers)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        repo.UpdateOrder(orderedNumbers);
    }

    /// <summary>従業員を削除する。</summary>
    public void DeleteEmployee(int number)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        repo.Delete(number);
    }

    /// <summary>性別(017)・就退区分(014)などのコード選択肢と、部門/役職/銀行の選択肢をまとめて返す。</summary>
    public EmployeeFormOptions GetEmployeeFormOptions()
    {
        if (!OperatingSystem.IsWindows()) return new EmployeeFormOptions();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var company = new CompanyRepository(store.Connection);
        company.EnsureSchema();
        var emp = new EmployeeRepository(store.Connection);
        emp.EnsureSchema();
        return new EmployeeFormOptions
        {
            性別 = company.GetCodeOptions("017"),
            就退区分 = company.GetCodeOptions("014"),
            部門 = company.GetDepartmentOptions(),
            役職 = company.GetPositionOptions(),
            銀行 = company.GetBankOptions(),
            所得税源泉 = company.GetCodeOptions("010"),
            障害者 = company.GetCodeOptions("015"),
            寡婦夫 = company.GetCodeOptions("006"),
            勤労学生 = company.GetCodeOptions("008"),
            配偶者区分 = company.GetCodeOptions("001"),
            配偶者障害者 = company.GetCodeOptions("019"),
            合計所得見積 = company.GetCodeOptions("013"),
        };
    }

    // ===== 支払日 =====

    /// <summary>支払日情報を読み込む。</summary>
    public PayDayInfo LoadPayDays()
    {
        if (!OperatingSystem.IsWindows()) return new PayDayInfo();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayDayRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load();
    }

    /// <summary>ダッシュボード用：支給日情報の登録有無・処理年・処理済みの月（対象労働月）を取得する。</summary>
    public PayDayStatus LoadPayDayStatus()
    {
        if (!OperatingSystem.IsWindows()) return new PayDayStatus(false, DateTime.Now.Year, Array.Empty<int>());
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayDayRepository(store.Connection);
        repo.EnsureSchema();
        var p = repo.Load();

        bool 登録済 = p.月別.Any(m => m.対象月 is not null || m.支給月 is not null);
        var 処理済月 = p.月別
            .Where(m => m.処理済みフラグ == "1")
            .Select(m => m.対象月 ?? m.支給月)
            .Where(v => v is not null).Select(v => v!.Value)
            .ToList();
        return new PayDayStatus(登録済, p.支給年, 処理済月);
    }

    /// <summary>支払日情報を保存し、会社情報の支給日登録フラグを立てる（現行同様）。</summary>
    public void SavePayDays(PayDayInfo info)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayDayRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(info);

        // 会社情報が存在すれば支給日登録フラグを立てる。
        new CompanyRepository(store.Connection).EnsureSchema();
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "UPDATE M_会社情報 SET 支給日登録フラグ = '1'";
        cmd.ExecuteNonQuery();
    }

    // ===== 支給・控除項目設定 =====

    /// <summary>支給・控除項目設定を読み込む。</summary>
    public PayItemSettings LoadPayItems()
    {
        if (!OperatingSystem.IsWindows()) return new PayItemSettings();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayItemRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load();
    }

    /// <summary>支給・控除項目設定を保存する。</summary>
    public void SavePayItems(PayItemSettings settings)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayItemRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(settings);
    }

    // ===== 保険料率 =====

    /// <summary>保険料率と保険料額表を読み込む。</summary>
    public RateBundle LoadRates()
    {
        if (!OperatingSystem.IsWindows()) return new RateBundle();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RateRepository(store.Connection);
        repo.EnsureSchema();
        return new RateBundle { 料率 = repo.Load(), 額表 = repo.LoadTable() };
    }

    /// <summary>保険料率と保険料額表を保存する。</summary>
    public void SaveRates(RateBundle bundle)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RateRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(bundle.料率);
        repo.SaveTable(bundle.額表);
    }

    // ===== 所得税（月額表） =====

    /// <summary>月額表（別表第1〜4・賞与甲乙）を読み込む。</summary>
    public TaxTableBundle LoadTaxTables()
    {
        if (!OperatingSystem.IsWindows()) return new TaxTableBundle();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new TaxTableRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load();
    }

    /// <summary>月額表（別表第1〜4・賞与甲乙・乙税第1〜3表/階差）を保存する。</summary>
    public void SaveTaxTables(TaxTableBundle bundle)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new TaxTableRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(bundle);
    }

    // ===== 給与処理（月次） =====

    /// <summary>給与処理画面の初期データ（手当/控除ラベル・従業員一覧・対象月情報）を支給回別に取得する。</summary>
    public PayrollMeta LoadPayrollMeta(int 支給回)
    {
        if (!OperatingSystem.IsWindows())
            return new PayrollMeta(Array.Empty<EmployeeListItem>(), Array.Empty<PayLabel>(), Array.Empty<string>(), "", "", "", null, Array.Empty<PayRoundOption>(), new Dictionary<string, string>(), Array.Empty<int>());

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        new CompanyRepository(store.Connection).EnsureSchema();
        var empRepo = new EmployeeRepository(store.Connection);
        empRepo.EnsureSchema();
        var items = new PayItemRepository(store.Connection).Load();
        var payday = new PayDayRepository(store.Connection).Load();
        var tranRepo = new PayrollTranRepository(store.Connection);
        tranRepo.EnsureSchema();

        var 手当 = items.給与手当
            .Select(a => new PayLabel(a.名称, a.非課税))
            .ToList();
        var 控除 = items.給与控除.Select(c => c.名称).ToList();

        MonthlyPayDay? m = (支給回 >= 1 && 支給回 <= payday.月別.Count) ? payday.月別[支給回 - 1] : null;
        string 対象年 = m?.対象年?.ToString() ?? "";
        string 対象月 = m?.対象月?.ToString() ?? "";
        string 支給日 = m is { 支給月: not null, 支給日: not null }
            ? $"{payday.支給年}/{m.支給月}/{m.支給日}" : "";

        // 支給回（処理月）の選択肢＝支給日登録の計算対象労働月（12回分）。
        var 候補 = new List<PayRoundOption>();
        for (int k = 1; k <= 12; k++)
        {
            MonthlyPayDay? mk = k <= payday.月別.Count ? payday.月別[k - 1] : null;
            bool 登録済 = mk?.対象年 is not null && mk?.対象月 is not null;
            候補.Add(new PayRoundOption(k, mk?.対象年, mk?.対象月, mk?.支給月, 登録済));
        }

        // 当該支給回で保存済み（行が存在する）の従業員Number（計算済みマーク用）。
        var 保存済 = tranRepo.ListSavedNumbers(支給回)
            .Select(n => int.TryParse(n, out int v) ? v : (int?)null)
            .Where(v => v is not null).Select(v => v!.Value).ToList();

        return new PayrollMeta(empRepo.List(), 手当, 控除, 対象年, 対象月, 支給日, m?.所定勤務日数, 候補, items.固定ラベル, 保存済);
    }

    /// <summary>
    /// 給与処理の初期表示すべき支給回（処理月）を返す。
    /// 支給日情報を見て「本日が支給年月日を過ぎていない最初の回」＝処理中の月を初期表示する。
    /// 全ての回の支給日を過ぎていれば最終登録回、未登録なら第1回。
    /// </summary>
    public int DefaultPayRound()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var payday = new PayDayRepository(store.Connection).Load();
        var today = DateTime.Today;
        int first = 0, last = 0;
        for (int k = 1; k <= payday.月別.Count; k++)
        {
            var m = payday.月別[k - 1];
            if (m.対象年 is null || m.対象月 is null) continue; // 未登録の回は対象外。
            if (first == 0) first = k;
            last = k;
            // 支給日が未到来（本日 <= 支給日）なら、その回を処理中とみなし初期表示。
            if (m.支給月 is int mm && m.支給日 is int dd && today <= SafeDate(payday.支給年, mm, dd))
                return k;
        }
        return last != 0 ? last : (first != 0 ? first : 1);
    }

    /// <summary>年月日から実在する日付を組み立てる（支給日が月末を超える設定でも例外にしない）。</summary>
    private static DateTime SafeDate(int year, int month, int day)
    {
        month = Math.Clamp(month, 1, 12);
        int daysInMonth = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, Math.Clamp(day, 1, daysInMonth));
    }

    /// <summary>指定従業員・支給回の給与トランを読み込む（無ければ Number/支給回 のみ設定した新規）。</summary>
    public PayrollEntry LoadPayrollEntry(string number, int 支給回)
    {
        var empty = new PayrollEntry { Number = number, 支給回 = 支給回 };
        if (!OperatingSystem.IsWindows()) return empty;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayrollTranRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(number, 支給回) ?? empty;
    }

    /// <summary>前月（支給回-1）の給与トランを読み込む（無ければ null）。現行「前月明細コピー」用。</summary>
    public PayrollEntry? LoadPreviousPayroll(string number, int 支給回)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (支給回 <= 1) return null; // 第1回には前月がない。
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayrollTranRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(number, 支給回 - 1);
    }

    /// <summary>残業時間から時間外手当を算出する（現行「時間外手当計算」ボタン）。</summary>
    public int ComputeOvertime(PayrollEntry input, int number)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var (proc, emp) = BuildCalc(store.Connection, number);
        return proc.ComputeOvertimeAllowance(input, emp);
    }

    /// <summary>時間外手当を区分別（略称＋金額）に算出する（「時間外手当計算」ボタンの内訳表示用）。合計＝各金額の合算。</summary>
    public IReadOnlyList<(string 略称, int 金額)> ComputeOvertimeParts(PayrollEntry input, int number)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<(string, int)>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var (proc, emp) = BuildCalc(store.Connection, number);
        return proc.ComputeOvertimeAllowanceParts(input, emp);
    }

    /// <summary>出勤日数と日給単価から基本給を算出する（現行「日給者賃金計算」ボタン）。</summary>
    public int ComputeDailyBasePay(double 出勤日数, int number)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var (proc, emp) = BuildCalc(store.Connection, number);
        return proc.ComputeDailyBasePay(出勤日数, emp);
    }

    /// <summary>入力から社会保険・所得税・各合計を確定する（現行「控除項目計算」＋所得税計算）。</summary>
    public PayrollEntry CalculatePayroll(PayrollEntry input, int number)
    {
        if (!OperatingSystem.IsWindows()) return input;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var (proc, emp) = BuildCalc(store.Connection, number);
        proc.Apply(input, emp);
        return input;
    }

    /// <summary>給与トランを保存する（UPSERT）。</summary>
    public void SavePayrollEntry(PayrollEntry entry)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayrollTranRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(entry);

        // 現行どおり M_支給日情報 の当該支給回 処理済みフラグを立てる（ダッシュボードの「処理済の月」用）。
        var payday = new PayDayRepository(store.Connection);
        payday.EnsureSchema();
        payday.MarkProcessed(entry.支給回);
    }

    /// <summary>計算器（料率/額表＋甲乙税表）と従業員設定を組み立てる。</summary>
    private static (PayrollProcessor, PayrollEmployeeSettings) BuildCalc(SqliteConnection conn, int number)
    {
        new CompanyRepository(conn).EnsureSchema();
        var rateRepo = new RateRepository(conn);
        rateRepo.EnsureSchema();
        var taxRepo = new TaxTableRepository(conn);
        taxRepo.EnsureSchema();
        var empRepo = new EmployeeRepository(conn);
        empRepo.EnsureSchema();

        var bundle = new RateBundle { 料率 = rateRepo.Load(), 額表 = rateRepo.LoadTable() };
        var legacy = new LegacyMasterRepository(conn);
        var 甲表 = legacy.LoadWithholdingTaxTables();

        // 乙税（月次）表は移行済みDBにのみ存在し得る。無ければ乙欄は計算不可（甲は可）。
        OtsuWithholdingTaxCalculator? 乙 = null;
        try { 乙 = new OtsuWithholdingTaxCalculator(legacy.LoadOtsuTaxTables()); }
        catch { /* 乙税表が無い環境では null（乙欄選択時に例外で通知）*/ }

        var processor = new PayrollProcessor(bundle, 甲表, 乙);

        Employee e = empRepo.Load(number) ?? new Employee();
        var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
        bool[] 非課税 = new PayItemRepository(conn).Load().給与手当.Select(a => a.非課税).ToArray();
        if (非課税.Length < 12) Array.Resize(ref 非課税, 12);

        var settings = new PayrollEmployeeSettings
        {
            健康保険加入 = e.健康保険 == "1",
            介護保険加入 = e.介護保険 == "1",
            子育支援加入 = e.子育支援 == "1",
            厚生年金加入 = e.厚生年金 == "1",
            年金基金加入 = e.年金基金 == "1",
            雇用保険加入 = e.雇用保険 == "1",
            報酬月額 = e.報酬月額,
            労働保険適用 = company.労働保険適用事業所,
            源泉区分 = e.所得税源泉 == "2" ? PayCalcDXPro.Data.Payroll.源泉区分.乙 : e.所得税源泉 == "3" ? PayCalcDXPro.Data.Payroll.源泉区分.計算しない : PayCalcDXPro.Data.Payroll.源泉区分.甲,
            源泉扶養親族数 = e.源泉税額計算の扶養親族数,
            端数処理 = int.TryParse(company.残業手当計算端数処理, out int h) ? h : 2,
            日給単価 = e.日給単価,
            法定内残業単価 = e.法定内残業単価,
            普通残業単価 = e.普通残業単価, 普通残業単価60h超 = e.普通残業単価60h超,
            深夜残業単価 = e.深夜残業単価, 深夜残業単価60h超 = e.深夜残業単価60h超,
            休日残業単価 = e.休日残業単価, 休日残業単価60h超 = e.休日残業単価60h超,
            休日深夜残業単価 = e.休日深夜残業単価, 休日深夜残業単価60h超 = e.休日深夜残業単価60h超,
            非課税手当 = 非課税,
        };
        return (processor, settings);
    }

    // ===== 賞与処理 =====

    /// <summary>賞与処理画面の初期データ（賞与手当16/控除9ラベル・従業員一覧・支給予定）を回別に取得する。</summary>
    public BonusMeta LoadBonusMeta(int 回)
    {
        if (!OperatingSystem.IsWindows())
            return new BonusMeta(Array.Empty<EmployeeListItem>(), Array.Empty<string>(), Array.Empty<string>(), DateTime.Now.Year, null, null, Array.Empty<BonusRoundOption>(), new Dictionary<string, string>(), Array.Empty<int>());

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        new CompanyRepository(store.Connection).EnsureSchema();
        var empRepo = new EmployeeRepository(store.Connection);
        empRepo.EnsureSchema();
        var items = new PayItemRepository(store.Connection).Load();
        var payday = new PayDayRepository(store.Connection).Load();
        var tranRepo = new BonusTranRepository(store.Connection);
        tranRepo.EnsureSchema();

        var 手当 = items.賞与手当.Select(a => a.名称).ToList();   // 16項目
        var 控除 = items.賞与控除.Select(c => c.名称).ToList();   // 9項目

        BonusPayDay? b = (回 >= 1 && 回 <= payday.賞与.Count) ? payday.賞与[回 - 1] : null;

        // 賞与回（1〜4）の選択肢＝支給日登録の賞与支給予定。
        var 候補 = new List<BonusRoundOption>();
        for (int k = 1; k <= 4; k++)
        {
            BonusPayDay? bk = k <= payday.賞与.Count ? payday.賞与[k - 1] : null;
            bool 登録済 = bk?.支給月 is not null;
            候補.Add(new BonusRoundOption(k, bk?.支給月, bk?.支給日, 登録済));
        }

        // 当該回で保存済み（行が存在する）の従業員Number（計算済みマーク用）。
        var 保存済 = tranRepo.ListSavedNumbers(回)
            .Select(n => int.TryParse(n, out int v) ? v : (int?)null)
            .Where(v => v is not null).Select(v => v!.Value).ToList();

        return new BonusMeta(empRepo.List(), 手当, 控除, payday.支給年, b?.支給月, b?.支給日, 候補, items.固定ラベル, 保存済);
    }

    /// <summary>
    /// 賞与処理の初期表示すべき回を返す。給与と同じく「本日が支給年月日を過ぎていない最初の回」を初期表示する。
    /// 全ての回の支給日を過ぎていれば最終登録回、未登録なら第1回。
    /// </summary>
    public int DefaultBonusRound()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var payday = new PayDayRepository(store.Connection).Load();
        var today = DateTime.Today;
        int first = 0, last = 0;
        for (int k = 1; k <= payday.賞与.Count; k++)
        {
            var b = payday.賞与[k - 1];
            if (b.支給月 is not int mm) continue; // 未登録の回は対象外。
            if (first == 0) first = k;
            last = k;
            if (b.支給日 is int dd && today <= SafeDate(payday.支給年, mm, dd))
                return k;
        }
        return last != 0 ? last : (first != 0 ? first : 1);
    }

    /// <summary>指定従業員・回の賞与トランを読み込む（無ければ Number/回 のみ設定した新規）。</summary>
    public BonusEntry LoadBonusEntry(string number, int 回)
    {
        var empty = new BonusEntry { Number = number, 回 = 回 };
        if (!OperatingSystem.IsWindows()) return empty;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new BonusTranRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Load(number, 回) ?? empty;
    }

    /// <summary>入力から社会保険・所得税・各合計を確定する（現行「控除項目計算」＋賞与所得税計算）。</summary>
    public BonusEntry CalculateBonus(BonusEntry input, int number, int 回)
    {
        if (!OperatingSystem.IsWindows()) return input;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var (proc, emp) = BuildBonusCalc(store.Connection, number);
        var ctx = BuildBonusContext(store.Connection, number.ToString(), 回);
        proc.Apply(input, emp, ctx);
        return input;
    }

    /// <summary>賞与トランを保存する（UPSERT）。</summary>
    public void SaveBonusEntry(BonusEntry entry)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new BonusTranRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(entry);
    }

    /// <summary>賞与計算器（料率＋賞与算出率表 甲/乙）と従業員設定を組み立てる。</summary>
    private static (BonusProcessor, BonusEmployeeSettings) BuildBonusCalc(SqliteConnection conn, int number)
    {
        new CompanyRepository(conn).EnsureSchema();
        var rateRepo = new RateRepository(conn);
        rateRepo.EnsureSchema();
        var empRepo = new EmployeeRepository(conn);
        empRepo.EnsureSchema();

        var 料率 = rateRepo.Load();
        var legacy = new LegacyMasterRepository(conn);
        var 甲 = legacy.LoadBonusTaxRateTable();

        // 賞与乙税表は移行済みDBにのみ存在し得る。無ければ乙欄は計算不可（甲は可）。
        PayCalcDXPro.Domain.Models.BonusOtsuRateTable? 乙 = null;
        try { 乙 = legacy.LoadBonusOtsuRateTable(); }
        catch { /* 乙税表が無い環境では null（乙欄選択時に例外で通知）*/ }

        var processor = new BonusProcessor(料率, 甲, 乙);

        Employee e = empRepo.Load(number) ?? new Employee();
        var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();

        var settings = new BonusEmployeeSettings
        {
            健康保険加入 = e.健康保険 == "1",
            介護保険加入 = e.介護保険 == "1",
            子育支援加入 = e.子育支援 == "1",
            厚生年金加入 = e.厚生年金 == "1",
            年金基金加入 = e.年金基金 == "1",
            雇用保険加入 = e.雇用保険 == "1",
            労働保険適用 = company.労働保険適用事業所,
            源泉区分 = e.所得税源泉 == "2" ? PayCalcDXPro.Data.Payroll.源泉区分.乙 : e.所得税源泉 == "3" ? PayCalcDXPro.Data.Payroll.源泉区分.計算しない : PayCalcDXPro.Data.Payroll.源泉区分.甲,
            源泉扶養親族数 = e.源泉税額計算の扶養親族数,
        };
        return (processor, settings);
    }

    /// <summary>
    /// 当回の賞与計算に必要な「前回までの情報」を組み立てる。
    /// 既支給賞与＝同年度内で当回より前の各回の総支給。前月給与課税対象額＝賞与支給月の前月に当たる給与支給回の課税対象額。
    /// </summary>
    private static BonusContext BuildBonusContext(SqliteConnection conn, string number, int 回)
    {
        var bonusRepo = new BonusTranRepository(conn);
        bonusRepo.EnsureSchema();
        var 既支給 = bonusRepo.ListEarlierTotals(number, 回);

        return new BonusContext
        {
            既支給賞与総支給 = 既支給,
            前月給与課税対象額 = LoadPrevMonthTaxable(conn, number, 回),
        };
    }

    /// <summary>賞与支給月の前月に当たる給与支給回の T_給与トラン.課税対象額を取得する（現行 F_賞与処理 の前月給与参照）。</summary>
    private static int? LoadPrevMonthTaxable(SqliteConnection conn, string number, int 回)
    {
        var payday = new PayDayRepository(conn).Load();
        int? 賞与支給月 = (回 >= 1 && 回 <= payday.賞与.Count) ? payday.賞与[回 - 1].支給月 : null;
        if (賞与支給月 is null) return null;

        // 賞与支給月の前月（現行どおり 支給月−1。1月賞与は0となり一致なし＝前月給与なし）。
        int 前月 = 賞与支給月.Value - 1;
        int? 給与支給回 = null;
        for (int k = 0; k < payday.月別.Count; k++)
        {
            if (payday.月別[k].支給月 == 前月) { 給与支給回 = k + 1; break; }
        }
        if (給与支給回 is null) return null;

        var payrollRepo = new PayrollTranRepository(conn);
        payrollRepo.EnsureSchema();
        return payrollRepo.Load(number, 給与支給回.Value)?.課税対象額;
    }

    // ===== 支払明細書（給与） =====

    /// <summary>
    /// 給与明細書PDFを生成する（現行 F_支払明細書）。
    /// numbers が空なら保存済み全員。twoPerPage=true で「A4に2明細」。保存済みのみ対象（賃金なしは現行同様除外）。
    /// </summary>
    public byte[] BuildPayrollPayslips(int 支給回, IReadOnlyList<int> numbers, bool twoPerPage, bool includeEmpty = false)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var meta = LoadPayrollMeta(支給回);
        string 会社名 = LoadCompany().会社名;
        var payday = LoadPayDays();
        string 締日 = FormatClosingDay(payday);
        bool 隠ゼロ = LoadPayItems().項目名印字;   // 金額0円の項目は項目名を印字しない（M_支給控除項目設定）

        var order = meta.従業員.Select(x => x.Number).ToList();
        // includeEmpty=賃金が発生していない従業員も含める（給与トランが無くても空明細を出力）。
        var target = SelectPayslipTargets(meta.従業員, meta.保存済従業員, numbers, includeEmpty, order);
        if (target.Count == 0) return Array.Empty<byte>();

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayrollTranRepository(store.Connection);
        repo.EnsureSchema();
        var emps = meta.従業員.ToDictionary(x => x.Number, x => x);

        var models = new List<PaySlipModel>();
        foreach (var n in target)
        {
            var e = repo.Load(n.ToString(), 支給回);
            if (e is null)
            {
                if (!includeEmpty) continue;
                e = new PayrollEntry { Number = n.ToString(), 支給回 = 支給回 };   // 賃金なし＝空明細（金額は0/空欄）
            }
            emps.TryGetValue(n, out var emp);
            models.Add(BuildPayrollPaySlipModel(meta, 会社名, 締日, e, emp, 隠ゼロ));
        }
        return models.Count == 0 ? Array.Empty<byte>() : new PaySlipPdfGenerator().Generate(models, twoPerPage);
    }

    /// <summary>明細書の出力対象 Number を決める。includeEmpty=true なら保存有無を問わず（在籍者＝退職以外＋保存済）を対象に、空指定は全対象。</summary>
    private static List<int> SelectPayslipTargets(
        IReadOnlyList<EmployeeListItem> 従業員, IReadOnlyList<int> 保存済, IReadOnlyList<int> numbers, bool includeEmpty, List<int> order)
    {
        IEnumerable<int> pool;
        if (includeEmpty)
        {
            var 候補 = 従業員.Where(x => 保存済.Contains(x.Number) || !x.退職).Select(x => x.Number).ToHashSet();
            pool = (numbers is { Count: > 0 } ? numbers.Where(候補.Contains) : 候補);
        }
        else
        {
            pool = (numbers is { Count: > 0 } ? numbers : 保存済).Where(保存済.Contains);
        }
        return pool.Distinct()
            .OrderBy(n => { int i = order.IndexOf(n); return i < 0 ? int.MaxValue : i; })
            .ToList();
    }

    /// <summary>締日表示（例「前月末日締め」）を支給日情報から組み立てる。</summary>
    private static string FormatClosingDay(PayDayInfo p)
    {
        if (string.IsNullOrEmpty(p.締日付)) return "";
        string day = p.締日付 == "末" ? "末日締め" : $"{p.締日付}日締め";
        return $"{p.締月}{day}";
    }

    private static int? NZ(int? v) => (v ?? 0) == 0 ? (int?)null : v;   // 0/未入力は空セル表示

    /// <summary>「0円は項目名を印字しない」設定が有効なら、金額0/未入力の非合計セルをラベルごと空にする（現行 R_給明細／R_賞明細 のラベル動的プレースホルダ挙動）。</summary>
    private static List<PaySlipLine> HideZeroNames(List<PaySlipLine> lines, bool 隠ゼロ)
        => !隠ゼロ ? lines
            : lines.Select(l => (!l.合計 && l.金額 is null) ? l with { 名称 = "" } : l).ToList();

    /// <summary>給与トラン＋メタ＋従業員から明細書の描画モデルを組み立てる（現行 R_給明細 の固定セル配置）。</summary>
    private static PaySlipModel BuildPayrollPaySlipModel(PayrollMeta meta, string 会社名, string 締日, PayrollEntry e, EmployeeListItem? emp, bool 隠ゼロ)
    {
        string L(string key, string def)
            => meta.固定ラベル.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;

        // 手当スロット（マスタ名がある枠のみ。無ければ空セル）。
        PaySlipLine 手当(int i) => (i < meta.手当ラベル.Count && !string.IsNullOrWhiteSpace(meta.手当ラベル[i].名称))
            ? new(meta.手当ラベル[i].名称 + (meta.手当ラベル[i].非課税 ? "（非）" : ""), NZ(e.給与支給手当[i]))
            : new("", null);
        PaySlipLine 控(int i) => (i < meta.控除ラベル.Count && !string.IsNullOrWhiteSpace(meta.控除ラベル[i]))
            ? new(meta.控除ラベル[i], NZ(e.控除項目[i])) : new("", null);
        var 空 = new PaySlipLine("", null);

        // 支給：基本給／手当1-12／(空,空)／時間外・勤怠控除・課税通勤・非課税通勤／総支給額（R_給明細 sheet2）。
        var 支給 = new List<PaySlipLine>
        {
            new(L("基本給", "基本給"), NZ(e.基本給)),
            手当(0), 手当(1), 手当(2), 手当(3),
            手当(4), 手当(5), 手当(6), 手当(7), 手当(8),
            手当(9), 手当(10), 手当(11), 空, 空,
            new(L("時間外手当", "時間外手当"), NZ(e.時間外手当)),
            new(L("勤怠控除", "勤怠控除"), (e.勤怠控除 ?? 0) == 0 ? (int?)null : -(e.勤怠控除!.Value)),
            new(L("課税通勤費", "課税通勤費"), NZ(e.課税通勤費)),
            new(L("非課税通勤費", "非課税通勤費"), NZ(e.非課税通勤費)),
            new("総支給額", e.総支給額 ?? 0, true),
        };

        // 控除：健保/介護/子育支援/厚年/年金基金／小規模共済/雇用/社保合計/課税対象/所得税／住民税,控除1-4／控除5-8,総控除額。
        var 控除 = new List<PaySlipLine>
        {
            new("健康保険", NZ(e.健康保険)), new("介護保険", NZ(e.介護保険)), new("子育支援", NZ(e.子育支援)),
            new("厚生年金", NZ(e.厚生年金)), new("年金基金", NZ(e.年金基金)),
            new("小規模共済", NZ(e.小規模共済)), new("雇用保険", NZ(e.雇用保険)), new("社会保険合計", NZ(e.社会保険合計)),
            new("課税対象額", NZ(e.課税対象額)), new("所得税", NZ(e.所得税)),
            new("住民税", NZ(e.住民税)), 控(0), 控(1), 控(2), 控(3),
            控(4), 控(5), 控(6), 控(7),
            new("総控除額", e.総控除額 ?? 0, true),
        };

        // 勤怠：3行15枠（最終行は法定内＋4種(60h超)）。R_給明細 sheet2 の値プレースホルダに合わせる。
        static string FD(double? v) => (v ?? 0) == 0 ? "" : v!.Value.ToString("0.##");
        var 勤怠 = new List<PaySlipFact>
        {
            new("所定出勤日数", FD(e.所定出勤日数)), new("出勤日数", FD(e.出勤日数)), new("労働時間", FD(e.労働時間)),
            new("欠勤(一般)日数", FD(e.欠勤日数_一般)), new("欠勤(病気)日数", FD(e.欠勤日数_病気等)),
            new("特別休暇", FD(e.特別休暇)), new("代休", FD(e.代休)), new("有給休暇", FD(e.有給休暇)),
            new("有給残日数", FD(e.有給残日数)), new("遅刻・早退", FD(e.遅刻早退)),
            new("法定内残業時間", FD(e.法定内残業)),
            new("普通残業時間(60h超)", FD(e.普通残業60h以下), FD(e.普通残業60h超)),
            new("深夜残業時間(60h超)", FD(e.深夜残業60h以下), FD(e.深夜残業60h超)),
            new("休日出勤時間(60h超)", FD(e.休日出勤60h以下), FD(e.休日出勤60h超)),
            new("休日深夜時間(60h超)", FD(e.休日深夜60h以下), FD(e.休日深夜60h超)),
        };

        return new PaySlipModel
        {
            タイトル = "給与支給明細書",
            対象表示 = string.IsNullOrEmpty(meta.対象月) ? "" : $"{meta.対象年}年{meta.対象月}月分",
            支給日表示 = meta.支給日 ?? "",
            締日表示 = 締日,
            会社名 = 会社名,
            社員番号 = emp?.No ?? "",
            従業員名 = emp?.氏名 ?? "",
            所属 = emp?.部門名 ?? "",
            支給 = HideZeroNames(支給, 隠ゼロ), 控除 = HideZeroNames(控除, 隠ゼロ), 勤怠 = 勤怠,
            年末調整還付金 = e.還付金額 ?? 0,
            年末調整徴収金 = e.徴収金額 ?? 0,
            差引支給額 = e.差引支給額 ?? 0,
            備考 = e.備考 ?? "",
        };
    }

    // ===== 支払明細書（賞与） =====

    /// <summary>賞与明細書PDFを生成する。numbers が空なら保存済み全員。twoPerPage=true で「A4に2明細」。includeEmpty=賃金（賞与）なしも空明細で含める。</summary>
    public byte[] BuildBonusPayslips(int 回, IReadOnlyList<int> numbers, bool twoPerPage, bool includeEmpty = false)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var meta = LoadBonusMeta(回);
        string 会社名 = LoadCompany().会社名;
        bool 隠ゼロ = LoadPayItems().項目名印字;   // 金額0円の項目は項目名を印字しない（M_支給控除項目設定）

        var order = meta.従業員.Select(x => x.Number).ToList();
        var target = SelectPayslipTargets(meta.従業員, meta.保存済従業員, numbers, includeEmpty, order);
        if (target.Count == 0) return Array.Empty<byte>();

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new BonusTranRepository(store.Connection);
        repo.EnsureSchema();
        var emps = meta.従業員.ToDictionary(x => x.Number, x => x);

        var models = new List<PaySlipModel>();
        foreach (var n in target)
        {
            var e = repo.Load(n.ToString(), 回);
            if (e is null)
            {
                if (!includeEmpty) continue;
                e = new BonusEntry { Number = n.ToString(), 回 = 回 };   // 賞与なし＝空明細
            }
            emps.TryGetValue(n, out var emp);
            models.Add(BuildBonusPaySlipModel(meta, 会社名, e, emp, 隠ゼロ));
        }
        return models.Count == 0 ? Array.Empty<byte>() : new PaySlipPdfGenerator().Generate(models, twoPerPage);
    }

    /// <summary>賞与トラン＋メタ＋従業員から明細書の描画モデルを組み立てる（現行 R_賞明細 sheet2＝勤怠・住民税なし、賞与＋支給16・控除1〜9）。</summary>
    private static PaySlipModel BuildBonusPaySlipModel(BonusMeta meta, string 会社名, BonusEntry e, EmployeeListItem? emp, bool 隠ゼロ)
    {
        string L(string key, string def)
            => meta.固定ラベル.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;

        // 賞与支給スロット（賞与手当1-16。マスタ名のある枠のみ）。賞与支給[i]＝手当1-12(0-11)＋任意1-4(12-15)。
        PaySlipLine 手当(int i) => (i < meta.手当ラベル.Count && !string.IsNullOrWhiteSpace(meta.手当ラベル[i]))
            ? new(meta.手当ラベル[i], NZ(e.賞与支給[i])) : new("", null);
        PaySlipLine 控(int i) => (i < meta.控除ラベル.Count && !string.IsNullOrWhiteSpace(meta.控除ラベル[i]))
            ? new(meta.控除ラベル[i], NZ(e.控除項目[i])) : new("", null);
        var 空 = new PaySlipLine("", null);

        // 支給：賞与／手当1-12／(空,空)／手当13-16(任意1-4)／総支給額（R_賞明細 sheet2）。
        var 支給 = new List<PaySlipLine>
        {
            new(L("賞与", "賞与"), NZ(e.賞与)),
            手当(0), 手当(1), 手当(2), 手当(3),
            手当(4), 手当(5), 手当(6), 手当(7), 手当(8),
            手当(9), 手当(10), 手当(11), 空, 空,
            手当(12), 手当(13), 手当(14), 手当(15),
            new("総支給額", e.総支給額 ?? 0, true),
        };

        // 控除：健保/介護/子育支援/厚年/年金基金／小規模共済/雇用/社保合計/課税対象/所得税／控除1-5／控除6-9,総控除額（賞与は住民税なし）。
        var 控除 = new List<PaySlipLine>
        {
            new("健康保険", NZ(e.健康保険)), new("介護保険", NZ(e.介護保険)), new("子育支援", NZ(e.子育支援)),
            new("厚生年金", NZ(e.厚生年金)), new("年金基金", NZ(e.年金基金)),
            new("小規模共済", NZ(e.小規模共済)), new("雇用保険", NZ(e.雇用保険)), new("社会保険合計", NZ(e.社会保険合計)),
            new("課税対象額", NZ(e.課税対象額)), new("所得税", NZ(e.所得税)),
            控(0), 控(1), 控(2), 控(3), 控(4),
            控(5), 控(6), 控(7), 控(8),
            new("総控除額", e.総控除額 ?? 0, true),
        };

        return new PaySlipModel
        {
            タイトル = "賞与支給明細書",
            対象表示 = meta.支給月 is int sm ? $"{meta.支給年}年{sm}月" : $"{meta.支給年}年",
            支給日表示 = meta.支給月 is int m2 && meta.支給日 is int d2 ? $"{meta.支給年}/{m2}/{d2}" : "",
            締日表示 = "",
            会社名 = 会社名,
            社員番号 = emp?.No ?? "",
            従業員名 = emp?.氏名 ?? "",
            所属 = emp?.部門名 ?? "",
            支給 = HideZeroNames(支給, 隠ゼロ), 控除 = HideZeroNames(控除, 隠ゼロ),
            年末調整還付金 = e.還付金額 ?? 0,
            年末調整徴収金 = e.徴収金額 ?? 0,
            差引支給額 = e.差引支給額 ?? 0,
            備考 = e.備考 ?? "",
        };
    }

    // ===== 導入設定の台帳印刷（現行 R_導入設定台帳／R_従業員台帳 のレイアウトに寄せる） =====

    /// <summary>導入設定台帳PDF（会社情報・部門/役職・銀行/提出先・支給日・支給控除項目）を生成する。</summary>
    public byte[] BuildSetupLedgerPdf()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var company = LoadCompany();
        var masters = LoadMasters();
        var payday = LoadPayDays();
        var items = LoadPayItems();

        static string P(string z) => z.Length == 7 ? $"{z[..3]}-{z[3..]}" : z;

        var 月別 = new List<(string, string, string, string)>();
        for (int i = 0; i < payday.月別.Count; i++)
        {
            var mm = payday.月別[i];
            if (mm.支給月 is null && mm.対象月 is null) continue;
            月別.Add((
                $"第{i + 1}回",
                mm.支給月 is int sm && mm.支給日 is int sd ? $"{sm}月{sd}日" : "",
                mm.対象年 is int y && mm.対象月 is int mo ? $"{y}年{mo}月分" : "",
                mm.所定勤務日数 is int nd ? $"{nd}日" : ""));
        }
        var 賞与 = new List<(string, string)>();
        for (int i = 0; i < payday.賞与.Count; i++)
        {
            var b = payday.賞与[i];
            if (b.支給月 is null) continue;
            賞与.Add(($"第{i + 1}回", b.支給月 is int bm && b.支給日 is int bd ? $"{bm}月{bd}日" : ""));
        }

        var model = new SetupLedgerModel
        {
            日付 = DateTime.Now.ToString("yyyy/MM/dd"),
            区分 = company.番号区分 == "2" ? "個人" : "法人",
            会社名カナ = company.会社名カナ, 会社名 = company.会社名,
            所在地カナ = company.所在地カナ, 所在地 = P(company.郵便番号) is { Length: > 0 } z && !string.IsNullOrEmpty(company.郵便番号) ? $"〒{z} {company.所在地}" : company.所在地,
            代表者氏名カナ = company.代表者氏名カナ, 代表者名 = company.代表者名,
            連絡者名カナ = company.連絡者名カナ, 連絡者名 = company.連絡者名,
            事業所名 = company.事業所名, 電話番号 = company.ＴＥＬ, 事業種目 = company.事業種目,
            マイナンバー = company.事業所マイナンバー, 税務署名 = company.税務署名,
            署番号 = company.署番号, 整理番号 = company.整理番号,
            支払方法 = company.支払方法, 支払金融機関 = company.支払金融機関, 金融機関所在地 = company.金融機関所在地,
            労働保険 = company.労働保険適用事業所 == "2" ? "一般以外" : "一般",
            年末調整方法 = company.年末調整方法,
            残業端数 = company.残業手当計算端数処理 switch { "1" => "四捨五入", "2" => "切捨", "3" => "切上", _ => company.残業手当計算端数処理 },
            部門 = masters.部門.Select(d => d.部門名).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
            役職 = masters.役職.Select(d => d.役職名).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
            銀行 = masters.銀行.Where(b => !string.IsNullOrWhiteSpace(b.銀行名)).Select(b => (b.銀行名, b.支店名)).ToList(),
            提出先 = masters.提出先.Where(r => !string.IsNullOrWhiteSpace(r.市区町村名)).Select(r => (r.市区町村名, r.徴収義務者番号)).ToList(),
            締月 = payday.締月, 締日付 = payday.締日付,
            基準支給日 = payday.支給日 is int kd ? $"{kd}日" : "",
            月別 = 月別, 賞与 = 賞与,
            給与手当 = items.給与手当.Select(a => (a.名称, a.非課税)).ToList(),
            給与控除 = items.給与控除.Select(a => a.名称).ToList(),
            賞与手当 = items.賞与手当.Select(a => a.名称).ToList(),
            賞与控除 = items.賞与控除.Select(a => a.名称).ToList(),
        };
        return new LedgerPdfGenerator().GenerateSetup(model);
    }

    /// <summary>従業員台帳PDF（現行 R_従業員台帳 準拠・1人1ページ）を生成する。numbers が空なら全従業員。</summary>
    public byte[] BuildEmployeeLedgerPdf(IReadOnlyList<int> numbers)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var opt = GetEmployeeFormOptions();
        var order = ListEmployees().Select(x => x.Number).ToList();
        var targets = (numbers is { Count: > 0 } ? numbers : order)
            .Where(order.Contains).Distinct()
            .OrderBy(n => order.IndexOf(n)).ToList();
        if (targets.Count == 0) return Array.Empty<byte>();

        static string R(IReadOnlyList<CodeOption> list, string code) => list.FirstOrDefault(o => o.コード == code)?.値 ?? code;
        static string Kanyu(string v) => v == "1" ? "加入" : "未加入";
        static string Yen(int v) => v.ToString("N0") + " 円";   // 金額には「円」
        static string Nin(int v) => v + " 人";                  // 人数には「人」
        static string P(string z) => z.Length == 7 ? $"{z[..3]}-{z[3..]}" : z;

        var list = new List<EmpLedgerModel>();
        foreach (var n in targets)
        {
            var e = LoadEmployee(n);
            if (e is null) continue;
            bool 現金 = e.銀行番号 == "1";
            string 預種 = e.預金種類 == "2" ? "当座" : "普通";
            list.Add(new EmpLedgerModel
            {
                氏名 = e.氏名, 氏名かな = e.フリガナ, 性別 = R(opt.性別, e.性別コード),
                生年月日 = e.生年月日, 従業員No = e.No, 郵便番号 = P(e.郵便番号),
                住所 = (e.住所１ + " " + e.住所２).Trim(), 勤務の状況 = R(opt.就退区分, e.就退区分),
                中途就職退職年月日 = e.就退年月日,
                // 事由＝就退区分の内容。継続勤務(=1)は非表示、中途就職/退職/死亡退職のみ表示。
                事由 = e.就退区分 == "1" ? "" : R(opt.就退区分, e.就退区分),

                部門 = R(opt.部門, e.部門番号), 職名 = R(opt.役職, e.役職番号),
                源泉所得税 = R(opt.所得税源泉, e.所得税源泉), 本人障害者 = R(opt.障害者, e.本人障害者),
                寡婦ひとり親 = R(opt.寡婦夫, e.本人寡婦夫), 勤労学生 = R(opt.勤労学生, e.本人勤労学生),
                配偶者区分 = R(opt.配偶者区分, e.配偶者区分), 配偶者障害者 = R(opt.配偶者障害者, e.配偶者障害者),
                配偶者合計所得 = R(opt.合計所得見積, e.配偶者合計所得見積),
                扶養親族数 = Nin(e.扶養親族親族数), 内同居親等 = Nin(e.扶養親族同居老親),
                内老人扶養 = Nin(e.扶養親族老人), 内特定扶養 = Nin(e.扶養親族特定),
                内特別障害 = Nin(e.扶養親族特別障害), 内一般障害 = Nin(e.扶養親族一般障害),
                特定親族 = Nin(e.特定親族数),
                住民税前期 = Yen(e.住民税1_5月), 住民税6月 = Yen(e.住民税6月), 住民税後期 = Yen(e.住民税7_12月),
                報酬月額 = Yen(e.報酬月額), 健康保険 = Kanyu(e.健康保険), 介護保険 = Kanyu(e.介護保険),
                子育支援 = Kanyu(e.子育支援), 厚生年金 = Kanyu(e.厚生年金), 年金基金 = Kanyu(e.年金基金), 雇用保険 = Kanyu(e.雇用保険),
                法定内 = Yen(e.法定内残業単価), 普通 = Yen(e.普通残業単価), 深夜 = Yen(e.深夜残業単価),
                休日出勤 = Yen(e.休日残業単価), 休日深夜 = Yen(e.休日深夜残業単価),
                普通超 = Yen(e.普通残業単価60h超), 深夜超 = Yen(e.深夜残業単価60h超),
                休日出勤超 = Yen(e.休日残業単価60h超), 休日深夜超 = Yen(e.休日深夜残業単価60h超),
                現金支給 = 現金 ? "●" : "", 口座振込 = 現金 ? "" : "●",
                支給口座情報 = 現金 ? "" : $"{R(opt.銀行, e.銀行番号)} {預種} {e.口座番号}".Trim(),
                日給単価 = Yen(e.日給単価),
            });
        }
        return list.Count == 0 ? Array.Empty<byte>() : new LedgerPdfGenerator().GenerateEmployees(list);
    }

    private static long Count(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        try { return (long)cmd.ExecuteScalar()!; } catch { return 0; }
    }

    // ===== 賃金台帳（支給日別）：現行 F_支払月分賃金台帳／R_給台帳月／R_賞台帳月 =====

    /// <summary>賃金台帳 印刷指示用の選択肢（給与回12・賞与回4・部門・既定選択＝最後に処理済みの回）を返す。</summary>
    public WageLedgerOptions GetWageLedgerOptions()
    {
        var payday = LoadPayDays();
        var masters = LoadMasters();

        var 給与 = new List<WageLedgerRound>();
        for (int k = 1; k <= 12; k++)
        {
            var mm = k <= payday.月別.Count ? payday.月別[k - 1] : null;
            bool 登録 = mm?.対象年 is not null && mm?.対象月 is not null;
            給与.Add(new WageLedgerRound("給与", k,
                登録 ? $"{mm!.対象年}年{mm.対象月}月分" : $"第{k}回", 登録, mm?.処理済みフラグ == "1"));
        }
        var 賞与 = new List<WageLedgerRound>();
        for (int k = 1; k <= 4; k++)
        {
            var b = k <= payday.賞与.Count ? payday.賞与[k - 1] : null;
            bool 登録 = b?.支給月 is not null;
            賞与.Add(new WageLedgerRound("賞与", k,
                登録 ? $"{payday.支給年}年{b!.支給月}月賞与" : $"第{k}回", 登録, b?.処理済みフラグ == "1"));
        }

        string 既定種別 = "給与";
        int 既定回 = (給与.FirstOrDefault(x => x.選択可) ?? 給与[0]).回;
        if (賞与.LastOrDefault(x => x.処理済) is { } sb) { 既定種別 = "賞与"; 既定回 = sb.回; }
        if (給与.LastOrDefault(x => x.処理済) is { } sg) { 既定種別 = "給与"; 既定回 = sg.回; }

        var 部門 = masters.部門.Select(d => d.部門名).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return new WageLedgerOptions(給与, 賞与, 部門, 既定種別, 既定回);
    }

    /// <summary>給与の賃金台帳PDF（R_給台帳月準拠）。部門名指定で絞り込み、withEmpty=true で賃金なしも含める。</summary>
    public byte[] BuildWageLedgerPdf(int 支給回, string? 部門名, bool withEmpty)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var company = LoadCompany();
        var payday = LoadPayDays();
        var items = LoadPayItems();
        var emps = ListEmployees();

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new PayrollTranRepository(store.Connection);
        repo.EnsureSchema();
        var byNumber = repo.ListByRound(支給回).GroupBy(e => e.Number).ToDictionary(g => g.Key, g => g.First());

        var cols = new List<(EmployeeListItem emp, PayrollEntry? e)>();
        foreach (var emp in emps)
        {
            if (!string.IsNullOrEmpty(部門名) && emp.部門名 != 部門名) continue;
            byNumber.TryGetValue(emp.Number.ToString(), out var e);
            bool has = e is not null && (e.データ個数 ?? 0) != 0;
            if (!withEmpty && !has) continue;
            cols.Add((emp, e));
        }

        string L(string key, string def) => items.固定ラベル.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;
        LedgerRow Money(string label, Func<PayrollEntry, int?> f, bool 太 = false)
            => new(label, cols.Select(c => c.e is null ? (double?)null : f(c.e) ?? 0).ToList(), true, 太);
        LedgerRow Att(string label, Func<PayrollEntry, double?> f, bool 太 = false)
            => new(label, cols.Select(c => c.e is null ? (double?)null : f(c.e) ?? 0).ToList(), false, 太);
        string 手当名(int i) => i < items.給与手当.Count && !string.IsNullOrWhiteSpace(items.給与手当[i].名称)
            ? items.給与手当[i].名称 + (items.給与手当[i].非課税 ? "（非）" : "") : "";
        string 控除名(int i) => i < items.給与控除.Count ? items.給与控除[i].名称 : "";

        var rows = new List<LedgerRow> { Money(L("基本給", "基本給"), e => e.基本給) };
        for (int i = 0; i < 12; i++) { int ii = i; rows.Add(Money(手当名(ii), e => e.給与支給手当[ii])); }
        rows.Add(Money(L("時間外手当", "時間外手当"), e => e.時間外手当));
        rows.Add(Money(L("勤怠控除", "勤怠控除"), e => e.勤怠控除));
        rows.Add(Money(L("課税通勤費", "課税通勤費"), e => e.課税通勤費));
        rows.Add(Money(L("非課税通勤費", "非課税通勤費"), e => e.非課税通勤費));
        rows.Add(Money("総支給額", e => e.総支給額, true));
        rows.Add(Money("健康保険", e => e.健康保険, true));
        rows.Add(Money("介護保険", e => e.介護保険));
        rows.Add(Money("子育支援", e => e.子育支援));
        rows.Add(Money("厚生年金", e => e.厚生年金));
        rows.Add(Money("年金基金", e => e.年金基金));
        rows.Add(Money("小規模共済", e => e.小規模共済));
        rows.Add(Money("雇用保険", e => e.雇用保険));
        rows.Add(Money("社会保険合計", e => e.社会保険合計, true));
        rows.Add(Money("課税対象額", e => e.課税対象額, true));
        rows.Add(Money("所得税", e => e.所得税));
        rows.Add(Money("住民税", e => e.住民税));
        for (int i = 0; i < 8; i++) { int ii = i; rows.Add(Money(控除名(ii), e => e.控除項目[ii])); }
        rows.Add(Money("総控除額", e => e.総控除額, true));
        rows.Add(Money("年末調整還付金", e => e.還付金額, true));
        rows.Add(Money("年末調整徴収金", e => e.徴収金額));
        rows.Add(Money("差引支給額", e => e.差引支給額, true));
        rows.Add(Att("所定出勤日数", e => e.所定出勤日数, true));
        rows.Add(Att("出勤日数", e => e.出勤日数));
        rows.Add(Att("労働時間", e => e.労働時間));
        rows.Add(Att("欠勤（一般）日数", e => e.欠勤日数_一般, true));
        rows.Add(Att("欠勤（病気）日数", e => e.欠勤日数_病気等));
        rows.Add(Att("特別休暇", e => e.特別休暇));
        rows.Add(Att("代休", e => e.代休));
        rows.Add(Att("有給休暇", e => e.有給休暇));
        rows.Add(Att("有給残日数", e => e.有給残日数));
        rows.Add(Att("遅刻・早退", e => e.遅刻早退));
        rows.Add(Att("法定内残業時間", e => e.法定内残業, true));
        rows.Add(Att("普通残業時間", e => e.普通残業60h以下));
        rows.Add(Att("深夜残業時間", e => e.深夜残業60h以下));
        rows.Add(Att("休日出勤時間", e => e.休日出勤60h以下));
        rows.Add(Att("休日深夜時間", e => e.休日深夜60h以下));
        rows.Add(Att("普通残業時間(60h超)", e => e.普通残業60h超, true));
        rows.Add(Att("深夜残業時間(60h超)", e => e.深夜残業60h超));
        rows.Add(Att("休日出勤時間(60h超)", e => e.休日出勤60h超));
        rows.Add(Att("休日深夜時間(60h超)", e => e.休日深夜60h超));

        var mm0 = (支給回 >= 1 && 支給回 <= payday.月別.Count) ? payday.月別[支給回 - 1] : null;
        var model = new WageLedgerModel
        {
            会社名 = company.会社名,
            部門 = 部門名 ?? "",
            年 = $"{payday.支給年}年",
            Is賞与 = false,
            支給見出し = mm0?.対象年 is int y && mm0.対象月 is int mo ? $"{y}年{mo}月分" : $"第{支給回}回",
            支給日 = mm0 is { 支給月: int sm, 支給日: int sd } ? $"{sm}月{sd}日支払" : "",
            締日 = FormatClosingDay(payday),
            従業員 = cols.Select(c => new LedgerEmployee(c.emp.No, c.emp.氏名)).ToList(),
            行 = rows,
        };
        return new WageLedgerPdfGenerator().Generate(model);
    }

    /// <summary>賞与の賃金台帳PDF（R_賞台帳月準拠）。部門名指定で絞り込み、withEmpty=true で賃金なしも含める。</summary>
    public byte[] BuildBonusLedgerPdf(int 賞与回, string? 部門名, bool withEmpty)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var company = LoadCompany();
        var payday = LoadPayDays();
        var items = LoadPayItems();
        var emps = ListEmployees();

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new BonusTranRepository(store.Connection);
        repo.EnsureSchema();
        var byNumber = repo.ListByRound(賞与回).GroupBy(e => e.Number).ToDictionary(g => g.Key, g => g.First());

        var cols = new List<(EmployeeListItem emp, BonusEntry? e)>();
        foreach (var emp in emps)
        {
            if (!string.IsNullOrEmpty(部門名) && emp.部門名 != 部門名) continue;
            byNumber.TryGetValue(emp.Number.ToString(), out var e);
            bool has = e is not null && (e.データ個数 ?? 0) != 0;
            if (!withEmpty && !has) continue;
            cols.Add((emp, e));
        }

        LedgerRow Money(string label, Func<BonusEntry, int?> f, bool 太 = false)
            => new(label, cols.Select(c => c.e is null ? (double?)null : f(c.e) ?? 0).ToList(), true, 太);
        string 手当名(int i) => i < items.賞与手当.Count ? items.賞与手当[i].名称 : "";   // 0..11=手当／12..15=任意項目
        string 控除名(int i) => i < items.賞与控除.Count ? items.賞与控除[i].名称 : "";

        var rows = new List<LedgerRow> { Money("賞与", e => e.賞与) };
        for (int i = 0; i < 12; i++) { int ii = i; rows.Add(Money(手当名(ii), e => e.賞与支給[ii])); }
        for (int i = 0; i < 4; i++) { int ii = i; rows.Add(Money(手当名(12 + ii), e => e.賞与支給[12 + ii])); }
        rows.Add(Money("総支給額", e => e.総支給額, true));
        rows.Add(Money("健康保険", e => e.健康保険, true));
        rows.Add(Money("介護保険", e => e.介護保険));
        rows.Add(Money("子育支援", e => e.子育支援));
        rows.Add(Money("厚生年金", e => e.厚生年金));
        rows.Add(Money("年金基金", e => e.年金基金));
        rows.Add(Money("小規模共済", e => e.小規模共済));
        rows.Add(Money("雇用保険", e => e.雇用保険));
        rows.Add(Money("社会保険合計", e => e.社会保険合計, true));
        rows.Add(Money("課税対象額", e => e.課税対象額, true));
        rows.Add(Money("所得税", e => e.所得税));
        for (int i = 0; i < 9; i++) { int ii = i; rows.Add(Money(控除名(ii), e => e.控除項目[ii])); }
        rows.Add(Money("総控除額", e => e.総控除額, true));
        rows.Add(Money("年末調整還付金", e => e.還付金額, true));
        rows.Add(Money("年末調整徴収金", e => e.徴収金額));
        rows.Add(Money("差引支給額", e => e.差引支給額, true));

        var b0 = (賞与回 >= 1 && 賞与回 <= payday.賞与.Count) ? payday.賞与[賞与回 - 1] : null;
        var model = new WageLedgerModel
        {
            会社名 = company.会社名,
            部門 = 部門名 ?? "",
            年 = $"{payday.支給年}年",
            Is賞与 = true,
            支給見出し = $"第{賞与回}回目賞与",
            支給日 = b0 is { 支給月: int sm, 支給日: int sd } ? $"{sm}月{sd}日支払" : "",
            締日 = "",
            従業員 = cols.Select(c => new LedgerEmployee(c.emp.No, c.emp.氏名)).ToList(),
            行 = rows,
        };
        return new WageLedgerPdfGenerator().Generate(model);
    }

    // ===== 賃金台帳（従業員別）：現行 R_給台帳従／R_賞台帳従 =====

    /// <summary>賃金台帳（従業員別）PDF。従業員1人を1ページとし、列＝支給回（月／賞与回）＋年間合計。</summary>
    /// <summary>賃金台帳（従業員別）の従業員選択リスト。当年に給与／賞与データのある従業員を「賃金あり」とする。</summary>
    public IReadOnlyList<WageLedgerEmpSel> GetWageLedgerEmpList(bool is賞与)
    {
        var emps = ListEmployees();
        var 賃金あり = new HashSet<int>();
        if (OperatingSystem.IsWindows())
        {
            using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
            if (!is賞与)
            {
                var repo = new PayrollTranRepository(store.Connection);
                repo.EnsureSchema();
                for (int k = 1; k <= 12; k++)
                    foreach (var e in repo.ListByRound(k))
                        if ((e.データ個数 ?? 0) != 0 && int.TryParse(e.Number, out var n)) 賃金あり.Add(n);
            }
            else
            {
                var repo = new BonusTranRepository(store.Connection);
                repo.EnsureSchema();
                for (int k = 1; k <= 4; k++)
                    foreach (var e in repo.ListByRound(k))
                        if ((e.データ個数 ?? 0) != 0 && int.TryParse(e.Number, out var n)) 賃金あり.Add(n);
            }
        }
        return emps.Select(emp => new WageLedgerEmpSel(
            emp.Number, emp.No, emp.氏名, emp.フリガナ, emp.部門名, emp.退職, 賃金あり.Contains(emp.Number))).ToList();
    }

    /// <param name="対象">出力対象の従業員 Number。null または空なら（部門・賃金なし条件に従い）全員。</param>
    public byte[] BuildWageLedgerEmpPdf(bool is賞与, string? 部門名, bool withEmpty, IReadOnlyCollection<int>? 対象 = null)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var company = LoadCompany();
        var payday = LoadPayDays();
        var items = LoadPayItems();
        var emps = ListEmployees();
        string? 部門 = string.IsNullOrWhiteSpace(部門名) ? null : 部門名;
        string 締日 = is賞与 ? "" : FormatClosingDay(payday);

        var pages = new List<WageLedgerEmpPage>();

        if (!is賞与)
        {
            // 列＝登録済みの月
            var rounds = new List<int>();
            var cols = new List<WageLedgerEmpCol>();
            for (int k = 1; k <= 12 && k <= payday.月別.Count; k++)
            {
                var mm = payday.月別[k - 1];
                if (mm.対象年 is int y && mm.対象月 is int mo)
                {
                    rounds.Add(k);
                    string 支日 = mm is { 支給月: int sm, 支給日: int sd } ? $"{sm}月{sd}日支払" : "";
                    cols.Add(new WageLedgerEmpCol($"{y}年{mo}月分", 支日));
                }
            }

            using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
            var repo = new PayrollTranRepository(store.Connection);
            repo.EnsureSchema();
            var byRound = rounds.ToDictionary(k => k,
                k => repo.ListByRound(k).GroupBy(e => e.Number).ToDictionary(g => g.Key, g => g.First()));

            string L(string key, string def) => items.固定ラベル.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;
            string 手当名(int i) => i < items.給与手当.Count && !string.IsNullOrWhiteSpace(items.給与手当[i].名称)
                ? items.給与手当[i].名称 + (items.給与手当[i].非課税 ? "（非）" : "") : "";
            string 控除名(int i) => i < items.給与控除.Count ? items.給与控除[i].名称 : "";

            foreach (var emp in emps)
            {
                if (部門 != null && emp.部門名 != 部門) continue;
                if (対象 is { Count: > 0 } && !対象.Contains(emp.Number)) continue;
                var es = rounds.Select(k => byRound[k].TryGetValue(emp.Number.ToString(), out var e) ? e : null).ToList();
                if (!withEmpty && !es.Any(e => e is not null && (e.データ個数 ?? 0) != 0)) continue;

                LedgerRow Money(string label, Func<PayrollEntry, int?> f, bool 太 = false)
                    => new(label, es.Select(e => e is null ? (double?)null : f(e) ?? 0).ToList(), true, 太);
                LedgerRow Att(string label, Func<PayrollEntry, double?> f, bool 太 = false)
                    => new(label, es.Select(e => e is null ? (double?)null : f(e) ?? 0).ToList(), false, 太);

                var rows = new List<LedgerRow> { Money(L("基本給", "基本給"), e => e.基本給) };
                for (int i = 0; i < 12; i++) { int ii = i; rows.Add(Money(手当名(ii), e => e.給与支給手当[ii])); }
                rows.Add(Money(L("時間外手当", "時間外手当"), e => e.時間外手当));
                rows.Add(Money(L("勤怠控除", "勤怠控除"), e => e.勤怠控除));
                rows.Add(Money(L("課税通勤費", "課税通勤費"), e => e.課税通勤費));
                rows.Add(Money(L("非課税通勤費", "非課税通勤費"), e => e.非課税通勤費));
                rows.Add(Money("総支給額", e => e.総支給額, true));
                rows.Add(Money("健康保険", e => e.健康保険, true));
                rows.Add(Money("介護保険", e => e.介護保険));
                rows.Add(Money("子育支援", e => e.子育支援));
                rows.Add(Money("厚生年金", e => e.厚生年金));
                rows.Add(Money("年金基金", e => e.年金基金));
                rows.Add(Money("小規模共済", e => e.小規模共済));
                rows.Add(Money("雇用保険", e => e.雇用保険));
                rows.Add(Money("社会保険合計", e => e.社会保険合計, true));
                rows.Add(Money("課税対象額", e => e.課税対象額, true));
                rows.Add(Money("所得税", e => e.所得税));
                rows.Add(Money("住民税", e => e.住民税));
                for (int i = 0; i < 8; i++) { int ii = i; rows.Add(Money(控除名(ii), e => e.控除項目[ii])); }
                rows.Add(Money("総控除額", e => e.総控除額, true));
                rows.Add(Money("年末調整還付金", e => e.還付金額, true));
                rows.Add(Money("年末調整徴収金", e => e.徴収金額));
                rows.Add(Money("差引支給額", e => e.差引支給額, true));
                rows.Add(Att("所定出勤日数", e => e.所定出勤日数, true));
                rows.Add(Att("出勤日数", e => e.出勤日数));
                rows.Add(Att("労働時間", e => e.労働時間));
                rows.Add(Att("欠勤（一般）日数", e => e.欠勤日数_一般, true));
                rows.Add(Att("欠勤（病気）日数", e => e.欠勤日数_病気等));
                rows.Add(Att("特別休暇", e => e.特別休暇));
                rows.Add(Att("代休", e => e.代休));
                rows.Add(Att("有給休暇", e => e.有給休暇));
                rows.Add(Att("有給残日数", e => e.有給残日数));
                rows.Add(Att("遅刻・早退", e => e.遅刻早退));
                rows.Add(Att("法定内残業時間", e => e.法定内残業, true));
                rows.Add(Att("普通残業時間", e => e.普通残業60h以下));
                rows.Add(Att("深夜残業時間", e => e.深夜残業60h以下));
                rows.Add(Att("休日出勤時間", e => e.休日出勤60h以下));
                rows.Add(Att("休日深夜時間", e => e.休日深夜60h以下));
                rows.Add(Att("普通残業時間(60h超)", e => e.普通残業60h超, true));
                rows.Add(Att("深夜残業時間(60h超)", e => e.深夜残業60h超));
                rows.Add(Att("休日出勤時間(60h超)", e => e.休日出勤60h超));
                rows.Add(Att("休日深夜時間(60h超)", e => e.休日深夜60h超));

                pages.Add(new WageLedgerEmpPage(emp.部門名, emp.No, emp.氏名, 締日, cols, rows));
            }
        }
        else
        {
            // 列＝賞与1〜4回目＋空列で給与と同じ12列に揃える（マイグレ元 R_賞台帳従 準拠）
            const int 列数 = 12;
            var cols = new List<WageLedgerEmpCol>();
            for (int k = 1; k <= 4; k++)
            {
                var b = k <= payday.賞与.Count ? payday.賞与[k - 1] : null;
                string 支日 = b is { 支給月: int sm, 支給日: int sd } ? $"{sm}月{sd}日支払" : "";
                cols.Add(new WageLedgerEmpCol($"賞与{k}回目", 支日));
            }
            for (int k = 5; k <= 列数; k++) cols.Add(new WageLedgerEmpCol("", ""));

            using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
            var repo = new BonusTranRepository(store.Connection);
            repo.EnsureSchema();
            var byRound = Enumerable.Range(1, 4).ToDictionary(k => k,
                k => repo.ListByRound(k).GroupBy(e => e.Number).ToDictionary(g => g.Key, g => g.First()));

            string 手当名(int i) => i < items.賞与手当.Count ? items.賞与手当[i].名称 : "";   // 0..11=手当／12..15=任意項目
            string 控除名(int i) => i < items.賞与控除.Count ? items.賞与控除[i].名称 : "";

            foreach (var emp in emps)
            {
                if (部門 != null && emp.部門名 != 部門) continue;
                if (対象 is { Count: > 0 } && !対象.Contains(emp.Number)) continue;
                var es = Enumerable.Range(1, 4).Select(k => byRound[k].TryGetValue(emp.Number.ToString(), out var e) ? e : null).ToList();
                if (!withEmpty && !es.Any(e => e is not null && (e.データ個数 ?? 0) != 0)) continue;

                LedgerRow Money(string label, Func<BonusEntry, int?> f, bool 太 = false)
                {
                    var vals = new List<double?>(列数);
                    foreach (var e in es) vals.Add(e is null ? (double?)null : f(e) ?? 0);
                    while (vals.Count < 列数) vals.Add(null);
                    return new(label, vals, true, 太);
                }

                var rows = new List<LedgerRow> { Money("賞与", e => e.賞与) };
                for (int i = 0; i < 12; i++) { int ii = i; rows.Add(Money(手当名(ii), e => e.賞与支給[ii])); }
                for (int i = 0; i < 4; i++) { int ii = i; rows.Add(Money(手当名(12 + ii), e => e.賞与支給[12 + ii])); }
                rows.Add(Money("総支給額", e => e.総支給額, true));
                rows.Add(Money("健康保険", e => e.健康保険, true));
                rows.Add(Money("介護保険", e => e.介護保険));
                rows.Add(Money("子育支援", e => e.子育支援));
                rows.Add(Money("厚生年金", e => e.厚生年金));
                rows.Add(Money("年金基金", e => e.年金基金));
                rows.Add(Money("小規模共済", e => e.小規模共済));
                rows.Add(Money("雇用保険", e => e.雇用保険));
                rows.Add(Money("社会保険合計", e => e.社会保険合計, true));
                rows.Add(Money("課税対象額", e => e.課税対象額, true));
                rows.Add(Money("所得税", e => e.所得税));
                for (int i = 0; i < 9; i++) { int ii = i; rows.Add(Money(控除名(ii), e => e.控除項目[ii])); }
                rows.Add(Money("総控除額", e => e.総控除額, true));
                rows.Add(Money("年末調整還付金", e => e.還付金額, true));
                rows.Add(Money("年末調整徴収金", e => e.徴収金額));
                rows.Add(Money("差引支給額", e => e.差引支給額, true));

                pages.Add(new WageLedgerEmpPage(emp.部門名, emp.No, emp.氏名, 締日, cols, rows));
            }
        }

        if (pages.Count == 0) return Array.Empty<byte>();
        var model = new WageLedgerEmpModel
        {
            会社名 = company.会社名,
            年 = $"{payday.支給年}年",
            Is賞与 = is賞与,
            区分ラベル = is賞与 ? "【賞 与】" : "【給 与】",
            ページ = pages,
        };
        return new WageLedgerEmpPdfGenerator().Generate(model);
    }

    // ===== 関連帳票：銀行振込一覧表（現行 R_銀行表） =====

    /// <summary>銀行振込一覧表PDF。指定処理回で処理済みの、現金支給以外（銀行振込）の従業員を一覧する。</summary>
    public byte[] BuildBankTransferPdf(bool is賞与, int 回)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var company = LoadCompany();
        var payday = LoadPayDays();

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var companyRepo = new CompanyRepository(store.Connection);
        companyRepo.EnsureSchema();
        var bankMap = companyRepo.GetBankMap();
        var empRepo = new EmployeeRepository(store.Connection);
        empRepo.EnsureSchema();

        // 振込金額（差引支給額）を Number→金額 で取得（当回に処理データがある従業員のみ）
        Dictionary<string, long> 金額;
        if (!is賞与)
        {
            var repo = new PayrollTranRepository(store.Connection); repo.EnsureSchema();
            金額 = repo.ListByRound(回).GroupBy(e => e.Number)
                .ToDictionary(g => g.Key, g => (long)(g.First().差引支給額 ?? 0));
        }
        else
        {
            var repo = new BonusTranRepository(store.Connection); repo.EnsureSchema();
            金額 = repo.ListByRound(回).GroupBy(e => e.Number)
                .ToDictionary(g => g.Key, g => (long)(g.First().差引支給額 ?? 0));
        }

        var rows = new List<BankTransferRow>();
        foreach (var item in empRepo.List())   // 並び順
        {
            if (!金額.TryGetValue(item.Number.ToString(), out var amt)) continue;   // 当回処理済みのみ
            var e = empRepo.Load(item.Number);
            if (e is null || e.銀行番号 == "1") continue;                            // 現金支給は対象外
            var (銀行名, 支店名) = bankMap.TryGetValue(e.銀行番号, out var b) ? b : ("", "");
            rows.Add(new BankTransferRow(
                銀行名, 支店名, e.預金種類 == "2" ? "当座" : "普通",
                e.口座番号, e.No, e.氏名, e.フリガナ, amt));
        }

        if (rows.Count == 0) return Array.Empty<byte>();

        string 見出し, 支日;
        if (!is賞与)
        {
            var mm = (回 >= 1 && 回 <= payday.月別.Count) ? payday.月別[回 - 1] : null;
            見出し = mm?.対象年 is int y && mm.対象月 is int mo ? $"{y}年{mo}月分" : $"第{回}回";
            支日 = mm is { 支給月: int sm, 支給日: int sd } ? $"{sm}月{sd}日支払" : "";
        }
        else
        {
            var b0 = (回 >= 1 && 回 <= payday.賞与.Count) ? payday.賞与[回 - 1] : null;
            見出し = b0 is { 支給月: int sm } ? $"{payday.支給年}年{sm}月賞与" : $"第{回}回目賞与";
            支日 = b0 is { 支給月: int sm2, 支給日: int sd2 } ? $"{sm2}月{sd2}日支払" : "";
        }

        var model = new BankTransferModel
        {
            会社名 = company.会社名,
            支給見出し = 見出し,
            支給日 = 支日,
            明細 = rows,
            合計件数 = rows.Count,
            合計金額 = rows.Sum(r => r.振込金額),
        };
        return new BankTransferPdfGenerator().Generate(model);
    }

    /// <summary>給与/賞与封筒印刷PDF（現行 R_封筒／R_封筒Y）。is賞与=false:給与/true:賞与。回=支給回。
    /// landscape=横長封筒。withEmpty=賃金なしも含める。numbers=対象従業員Number（空＝当回保存済み全員）。1名1ページ。</summary>
    public byte[] BuildEnvelopePdf(bool is賞与, int 回, bool landscape, bool withEmpty, IReadOnlyList<int> numbers)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var company = LoadCompany();
        var payday = LoadPayDays();

        // 従業員一覧・保存済み（明細書印刷と同じメタ）を支給回別に取得。
        IReadOnlyList<EmployeeListItem> emps;
        IReadOnlyList<int> 保存済;
        if (is賞与) { var meta = LoadBonusMeta(回); emps = meta.従業員; 保存済 = meta.保存済従業員; }
        else { var meta = LoadPayrollMeta(回); emps = meta.従業員; 保存済 = meta.保存済従業員; }

        // 対象選別は明細書印刷と完全一致（withEmptyで在籍者まで拡張・並び順は一覧順）。
        var order = emps.Select(x => x.Number).ToList();
        var target = SelectPayslipTargets(emps, 保存済, numbers, withEmpty, order);
        var byNum = emps.ToDictionary(x => x.Number, x => x);

        var 明細 = new List<EnvelopeRow>();
        foreach (var num in target)
        {
            if (!byNum.TryGetValue(num, out var emp)) continue;
            string 部門 = (emp.部門名 ?? "") == "未設定" ? "" : (emp.部門名 ?? "");
            明細.Add(new EnvelopeRow(emp.No, 部門, emp.氏名 ?? ""));
        }

        // 明細書名・支給日（封筒表記：給与「2026年6月分」／賞与「2026年6月 賞与」）。
        string 支給日;
        if (is賞与)
        {
            var b0 = (回 >= 1 && 回 <= payday.賞与.Count) ? payday.賞与[回 - 1] : null;
            支給日 = $"{payday.支給年}年{b0?.支給月}月 賞与";
        }
        else
        {
            var mm0 = (回 >= 1 && 回 <= payday.月別.Count) ? payday.月別[回 - 1] : null;
            支給日 = mm0?.対象年 is int y && mm0.対象月 is int mo ? $"{y}年{mo}月分" : $"第{回}回";
        }

        var model = new EnvelopeModel
        {
            会社名 = company.会社名,
            明細書名 = is賞与 ? "賞与明細書" : "給与明細書",
            支給日 = 支給日,
            横 = landscape,
            明細 = 明細,
        };
        return new EnvelopePdfGenerator().Generate(model);
    }

    // ===== 関連帳票：現金支給金種表（現行 R_金種表） =====

    /// <summary>現金支給金種表PDF。指定処理回で処理済みの現金支給者を対象に、支払金額の金種内訳を一覧する。</summary>
    public byte[] BuildKinsyuPdf(bool is賞与, int 回, bool include2000)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var company = LoadCompany();
        var payday = LoadPayDays();

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        new CompanyRepository(store.Connection).EnsureSchema();
        var empRepo = new EmployeeRepository(store.Connection);
        empRepo.EnsureSchema();

        Dictionary<string, long> 金額;
        if (!is賞与)
        {
            var repo = new PayrollTranRepository(store.Connection); repo.EnsureSchema();
            金額 = repo.ListByRound(回).GroupBy(e => e.Number)
                .ToDictionary(g => g.Key, g => (long)(g.First().差引支給額 ?? 0));
        }
        else
        {
            var repo = new BonusTranRepository(store.Connection); repo.EnsureSchema();
            金額 = repo.ListByRound(回).GroupBy(e => e.Number)
                .ToDictionary(g => g.Key, g => (long)(g.First().差引支給額 ?? 0));
        }

        var rows = new List<KinsyuRow>();
        var 合計金種 = new long[KinsyuModel.額面.Length];
        long 合計支払 = 0;
        foreach (var item in empRepo.List())   // 並び順
        {
            if (!金額.TryGetValue(item.Number.ToString(), out var amt)) continue;   // 当回処理済みのみ
            var e = empRepo.Load(item.Number);
            if (e is null || e.銀行番号 != "1") continue;                            // 現金支給のみ
            var 金種 = Breakdown(amt, include2000);
            rows.Add(new KinsyuRow(e.No, e.氏名, amt, 金種));
            合計支払 += amt;
            for (int k = 0; k < 金種.Length; k++) 合計金種[k] += 金種[k];
        }

        if (rows.Count == 0) return Array.Empty<byte>();

        string 見出し, 支日;
        if (!is賞与)
        {
            var mm = (回 >= 1 && 回 <= payday.月別.Count) ? payday.月別[回 - 1] : null;
            見出し = mm?.対象年 is int y && mm.対象月 is int mo ? $"{y}年{mo}月分" : $"第{回}回";
            支日 = mm is { 支給月: int sm, 支給日: int sd } ? $"{sm}月{sd}日支払" : "";
        }
        else
        {
            var b0 = (回 >= 1 && 回 <= payday.賞与.Count) ? payday.賞与[回 - 1] : null;
            見出し = b0 is { 支給月: int sm } ? $"{payday.支給年}年{sm}月賞与" : $"第{回}回目賞与";
            支日 = b0 is { 支給月: int sm2, 支給日: int sd2 } ? $"{sm2}月{sd2}日支払" : "";
        }

        var model = new KinsyuModel
        {
            会社名 = company.会社名,
            支給見出し = 見出し,
            支給日 = 支日,
            Include2000 = include2000,
            明細 = rows,
            合計支払金額 = 合計支払,
            合計金種 = 合計金種,
        };
        return new KinsyuPdfGenerator().Generate(model);
    }

    /// <summary>金額を金種（10000〜1円）に分解する。include2000=false なら2,000円札は使わない。</summary>
    private static int[] Breakdown(long amount, bool include2000)
    {
        var counts = new int[KinsyuModel.額面.Length];
        long rem = amount;
        for (int i = 0; i < KinsyuModel.額面.Length; i++)
        {
            int v = KinsyuModel.額面[i];
            if (v == 2000 && !include2000) continue;   // 2,000円札を使わない（枚数0）
            counts[i] = (int)(rem / v);
            rem %= v;
        }
        return counts;
    }

    // ===== 年末調整（従業員情報） =====

    /// <summary>年末調整の対象者一覧（在籍者を先・退職者を後ろ）。</summary>
    public IReadOnlyList<YearEndTarget> LoadYearEndTargets()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<YearEndTarget>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new YearEndRepository(store.Connection);
        repo.EnsureSchema();
        return repo.Targets();
    }

    /// <summary>1従業員の年末調整を読み込む（無ければ作成し、給与・賞与トランから年間集計を取り込む）。</summary>
    public YearEndEntry LoadYearEnd(int number)
    {
        if (!OperatingSystem.IsWindows()) return new YearEndEntry { Number = number };
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new YearEndRepository(store.Connection);
        repo.EnsureSchema();
        return repo.LoadOrCreate(number);
    }

    /// <summary>入力値から年末調整の年税額を計算し、結果を entry に反映する（保存はしない）。
    /// 配偶者・扶養親族の生データから人的控除等を自動算定してから年税額を計算する。</summary>
    public void ComputeYearEnd(YearEndEntry e)
    {
        if (!OperatingSystem.IsWindows()) return;
        // 前社収入・非課税通勤手当・前社社保/源泉の手入力を「計」へ反映してから計算する。
        YearEndCalculation.RecomputeTotals(e);

        // 乙欄＝「1」(年末調整する) 以外（未済「2」/乙欄「3」）は年調を行わず、結果欄をクリアして終了。
        if (e.乙欄 != "1")
        {
            YearEndCalculation.ClearYearEndResults(e);
            return;
        }

        // 年齢判定の基準年＝税年+1（現行 SYR_NEN＝処理年+1）。特定扶養(令和7年)＝2003/1/2〜2007/1/1生まれ 等の境界に合わせる。
        int 判定年 = LoadPayDayStatus().処理年 + 1;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        // 年末調整の法定マスタ（年額表・所得控除表・速算表 等）が未投入なら埋込SQLでシードする。
        new YearEndMasterRepository(store.Connection).EnsureSeeded();
        var legacy = new LegacyMasterRepository(store.Connection);
        var yearTables = legacy.LoadYearEndTables();
        var dedTables = legacy.LoadIncomeDeductionTables();

        // 配偶者・扶養親族 → 配偶者あり/所得/70歳・扶養控除等合計・特定親族特別控除額 を自動算定。
        YearEndCalculation.DeriveAutoDeductions(
            e, yearTables,
            legacy.LoadDependentDeductionTable(),
            legacy.LoadSpecialRelativeDeductionTable(),
            legacy.LoadPensionConversionTable(),
            判定年);

        YearEndCalculation.Apply(e, yearTables, dedTables);
    }

    /// <summary>年間集計に前社分・非課税通勤手当の手入力を反映して「計」を再計算する（DBアクセスなし・検証用）。</summary>
    public void RecomputeYearEndTotals(YearEndEntry e) => YearEndCalculation.RecomputeTotals(e);

    /// <summary>年末調整を保存する（入力＋計算結果。集計列は LoadOrCreate 時の Aggregate が管理）。</summary>
    public void SaveYearEnd(YearEndEntry e)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new YearEndRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(e);
    }

    /// <summary>
    /// 給与所得の源泉徴収票（現行 R_源泉徴収票）を1従業員分PDF化する。
    /// 年末調整（読込＋計算）・従業員・会社・マイナンバー・役職を収集し、<see cref="WithholdingSlipBuilder"/> で
    /// マーカー→値を組み立て、<see cref="WithholdingSlipReport"/> で官製様式に重ねる。
    /// ※Phase2：主要項目（ヘッダー/金額/配偶者/扶養明細・カウント/チェック/中途就退職/生年月日/摘要/支払者）を移植済。
    /// 　特定親族控除区分・非居住者詳細区分・障害配偶者控除・居住開始区分名・所得調整の（同配）（調整）摘要まで移植済。
    /// </summary>
    /// <summary>選択された複数従業員の源泉徴収票を1つの複数ページPDFにまとめる（現行 createReport(employeeNo[]) と同じく1帳票）。</summary>
    public byte[] BuildWithholdingSlipPdf(int[] numbers, string 種別, string 元号, string 年)
    {
        if (!OperatingSystem.IsWindows() || numbers is null || numbers.Length == 0) return Array.Empty<byte>();
        var employees = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var number in numbers)
        {
            var v = BuildWithholdingSlipValues(number, 種別, 元号, 年);
            if (v is not null) employees.Add(v);
        }
        return employees.Count == 0 ? Array.Empty<byte>() : new WithholdingSlipReport().CreatePdf(employees);
    }

    /// <summary>1従業員分の源泉徴収票（後方互換）。</summary>
    public byte[] BuildWithholdingSlipPdf(int number, string 種別, string 元号, string 年)
        => BuildWithholdingSlipPdf(new[] { number }, 種別, 元号, 年);

    /// <summary>
    /// 選択された複数従業員の給与支払報告書（個人別明細書）を1つの複数ページPDFにまとめる（現行 R_給与支払報告書）。
    /// マーカー→値は源泉徴収票と共通（<see cref="BuildWithholdingSlipValues"/>）で、様式のみ給報の背景に重ねる。
    /// 元号は給報様式では未使用（年度＝年分+1）。
    /// </summary>
    public byte[] BuildPayReportPdf(int[] numbers, string 種別, string 年)
    {
        if (!OperatingSystem.IsWindows() || numbers is null || numbers.Length == 0) return Array.Empty<byte>();
        var employees = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var number in numbers)
        {
            var v = BuildWithholdingSlipValues(number, 種別, "令和", 年);
            if (v is not null) employees.Add(v);
        }
        return employees.Count == 0 ? Array.Empty<byte>() : new PayReportReport().CreatePdf(employees);
    }

    /// <summary>1従業員分のマーカー→値辞書を組み立てる（該当従業員なしは null）。</summary>
    private IReadOnlyDictionary<string, string?>? BuildWithholdingSlipValues(int number, string 種別, string 元号, string 年)
    {
        if (!OperatingSystem.IsWindows()) return null;

        // 年末調整（読込→計算）。
        var e = LoadYearEnd(number);
        ComputeYearEnd(e);

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var emp = new EmployeeRepository(conn).Load(number);
        if (emp is null) return null;
        string 役職名 = ListEmployeesInternal(conn).FirstOrDefault(x => x.Number == number)?.役職名 ?? "";

        var companyRepo = new CompanyRepository(conn);
        var company = companyRepo.Load() ?? new CompanyInfo();
        int 判定年 = LoadPayDayStatus().処理年 + 1;

        var legacy = new LegacyMasterRepository(conn);
        // 扶養親族ごとの特定親族特別控除額（控除対象親族区分の特定親族区分判定に使用）。
        var 特定親族控除 = YearEndCalculation.特定親族控除内訳(e, legacy.LoadSpecialRelativeDeductionTable(), 判定年);
        // 障害配偶者控除額（障害者数カウント判定に使用）。
        int 障害配偶者控除 = YearEndCalculation.障害配偶者控除額(e, legacy.LoadDependentDeductionTable());
        // 扶養親族の収入要件（所得金額調整控除の（調整）摘要判定に使用）。
        var 扶養収入要件 = YearEndCalculation.扶養収入要件内訳(e, legacy.LoadPensionConversionTable(), 判定年);
        // 住宅借入金等特別控除区分名（M_コード 項目区分 003=新築購入／004=増改築）。
        string 新築購入区分名 = e.新築購入適用区分 is { Length: > 0 } c3
            ? companyRepo.GetCodeOptions("003").FirstOrDefault(o => o.コード == c3)?.値 ?? "" : "";
        string 増改築区分名 = e.増改築適用区分 is { Length: > 0 } c4
            ? companyRepo.GetCodeOptions("004").FirstOrDefault(o => o.コード == c4)?.値 ?? "" : "";

        // マイナンバー（復号。未登録は空）。
        string 本人 = "", 配偶者 = ""; var 扶養 = new string?[7];
        try
        {
            var mn = new MyNumberRepository(conn, GetMyNumberProtector(), new AuditLog(conn));
            mn.EnsureSchema();
            var rec = mn.GetDecrypted(number.ToString(), 操作者);
            本人 = rec.本人 ?? ""; 配偶者 = rec.配偶者 ?? "";
            for (int i = 0; i < 7 && i < rec.扶養親族.Count; i++) 扶養[i] = rec.扶養親族[i];
        }
        catch (KeyNotFoundException) { /* マイナンバー未登録 */ }

        var input = new WithholdingSlipInput
        {
            種別 = 種別, 元号 = 元号, 年 = 年,
            YearEnd = e, 判定年 = 判定年,
            受給者番号 = emp.No, 役職名 = 役職名,
            氏名 = emp.氏名, フリガナ = emp.フリガナ, 住所1 = emp.住所１, 住所2 = emp.住所２,
            生年月日 = emp.生年月日,
            本人番号 = 本人, 配偶者番号 = 配偶者, 扶養番号 = 扶養,
            会社名 = company.会社名, 会社所在地 = company.所在地, 会社電話 = company.ＴＥＬ,
            事業所番号 = company.事業所マイナンバー,
            所得金額調整控除適用の扶養者氏名 = e.所得金額調整控除適用の扶養者氏名,
            特定親族控除 = 特定親族控除,
            障害配偶者控除 = 障害配偶者控除,
            扶養収入要件 = 扶養収入要件,
            新築購入区分名 = 新築購入区分名,
            増改築区分名 = 増改築区分名,
        };

        return WithholdingSlipBuilder.Build(input);
    }

    /// <summary>
    /// 給与所得に対する源泉徴収簿（現行 R_徴収簿）を、選択された複数従業員分1つの複数ページPDF（1名＝1ページ）にまとめる。
    /// 年末調整（読込＋計算）・従業員・会社（年末調整方法）・支給日情報を収集し、<see cref="WithholdingLedgerBuilder"/> で
    /// マーカー→値を組み立て、<see cref="WithholdingLedgerReport"/> で官製様式に重ねる。定額減税欄は移植対象外。
    /// </summary>
    public byte[] BuildWithholdingLedgerPdf(int[] numbers, string 元号, string 年)
    {
        if (!OperatingSystem.IsWindows() || numbers is null || numbers.Length == 0) return Array.Empty<byte>();
        var employees = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var number in numbers)
        {
            var v = BuildWithholdingLedgerValues(number, 元号, 年);
            if (v is not null) employees.Add(v);
        }
        return employees.Count == 0 ? Array.Empty<byte>() : new WithholdingLedgerReport().CreatePdf(employees);
    }

    /// <summary>1従業員分の源泉徴収簿マーカー→値辞書を組み立てる（該当従業員なしは null）。</summary>
    private IReadOnlyDictionary<string, string?>? BuildWithholdingLedgerValues(int number, string 元号, string 年)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var e = LoadYearEnd(number);
        ComputeYearEnd(e);

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var conn = store.Connection;

        var emp = new EmployeeRepository(conn).Load(number);
        if (emp is null) return null;
        var listItem = ListEmployeesInternal(conn).FirstOrDefault(x => x.Number == number);

        var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
        int 判定年 = LoadPayDayStatus().処理年 + 1;

        // 支給日情報（月別・賞与の支給月/日）。スロット順に並ぶ。
        var pd = new PayDayRepository(conn).Load();
        var 支給月 = new int?[12]; var 支給日 = new int?[12];
        for (int i = 0; i < pd.月別.Count && i < 12; i++) { 支給月[i] = pd.月別[i].支給月; 支給日[i] = pd.月別[i].支給日; }
        var 賞与支給月 = new int?[4]; var 賞与支給日 = new int?[4];
        for (int i = 0; i < pd.賞与.Count && i < 4; i++) { 賞与支給月[i] = pd.賞与[i].支給月; 賞与支給日[i] = pd.賞与[i].支給日; }

        // 特定扶養／特定親族のカウント判定に使う特定親族特別控除額（源泉徴収票と同じ）。
        var legacy = new LegacyMasterRepository(conn);
        var 特定親族控除 = YearEndCalculation.特定親族控除内訳(e, legacy.LoadSpecialRelativeDeductionTable(), 判定年);

        var input = new WithholdingLedgerInput
        {
            元号 = 元号, 年 = 年,
            YearEnd = e, 判定年 = 判定年,
            所属 = listItem?.部門名 ?? "", 職名 = listItem?.役職名 ?? "", 整理番号 = emp.No,
            郵便番号 = emp.郵便番号, 住所 = emp.住所１ + emp.住所２,
            氏名 = emp.氏名, フリガナ = emp.フリガナ, 生年月日 = emp.生年月日,
            年末調整方法 = company.年末調整方法,
            支給月 = 支給月, 支給日 = 支給日, 賞与支給月 = 賞与支給月, 賞与支給日 = 賞与支給日,
            特定親族控除 = 特定親族控除,
        };

        return WithholdingLedgerBuilder.Build(input);
    }

    /// <summary>年末調整 集計表（納付書用）PDFを生成する（現行 R_集計表・全従業員を集計）。
    /// syukeiCheck=1 納期特例(7〜12月)/2 一般(12月のみ)、syouyoCheck=1〜5（含む賞与の範囲）。</summary>
    public byte[] BuildSummaryPdf(int syukeiCheck, int syouyoCheck)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();

        int 支給年;
        IReadOnlyList<YearEndTarget> targets;
        using (var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser))
        {
            var conn = store.Connection;
            支給年 = new PayDayRepository(conn).Load().支給年;
            var repo = new YearEndRepository(conn);
            repo.EnsureSchema();
            targets = repo.Targets();
        }

        var emps = new List<SummaryBuilder.Employee>(targets.Count);
        foreach (var t in targets)
        {
            var e = LoadYearEnd(t.Number);
            ComputeYearEnd(e);
            emps.Add(new SummaryBuilder.Employee(
                t.No, t.氏名, e.乙欄, e.過不足額, e.給与, e.源泉, e.賞与, e.源泉賞与));
        }

        var result = SummaryBuilder.Build(new SummaryBuilder.Input(支給年, syukeiCheck, syouyoCheck, emps));
        return new SummaryReport().CreatePdf(result.集計値, result.明細);
    }

    /// <summary>給与所得の源泉徴収票等の法定調書合計表PDFを生成する（現行 R_合計表）。
    /// 給与所得（全従業員＝名称ありの年末調整対象を集計済みで）＋退職源泉/報酬/不動産/譲受/あっせん＋会社情報から集計する。</summary>
    public byte[] BuildLegalRecordSummaryPdf()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();

        int 支給年;
        CompanyInfo company;
        IReadOnlyList<YearEndTarget> targets;
        IReadOnlyList<退職支払行> 退職;
        IReadOnlyList<報酬支払行> 報酬;
        IReadOnlyList<不動産支払行> 不動産, 譲受, あっせん;
        using (var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser))
        {
            var conn = store.Connection;
            支給年 = new PayDayRepository(conn).Load().支給年;
            company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
            var ye = new YearEndRepository(conn);
            ye.EnsureSchema();
            targets = ye.Targets();

            退職 = LegalRecordSummaryBuilder.退職(conn);
            報酬 = LegalRecordSummaryBuilder.報酬(conn);
            不動産 = LegalRecordSummaryBuilder.不動産(conn);
            譲受 = LegalRecordSummaryBuilder.譲受(conn);
            あっせん = LegalRecordSummaryBuilder.あっせん(conn);
        }

        // 給与所得＝名称ありの各従業員を年末調整計算済みで集計（現行 T_従業員×T_年末調整・氏名NOT IN('')）。
        var 給与 = new List<給与支払行>(targets.Count);
        foreach (var t in targets)
        {
            if (string.IsNullOrEmpty(t.氏名)) continue;
            var e = LoadYearEnd(t.Number);
            ComputeYearEnd(e);
            給与.Add(LegalRecordSummaryBuilder.給与行(e));
        }

        var summary = new LegalRecordSummaryCalculator().Calculate(給与, 退職, 報酬, 不動産, 譲受, あっせん);
        var 提出者 = LegalRecordSummaryBuilder.提出者(company, 支給年);
        return new LegalRecordSummaryReport().CreatePdf(summary, 提出者);
    }

    /// <summary>
    /// 給与支払報告書（総括表）（現行 R_総括表）を全社集計でPDF化する（引数なし・A4横1面・4面付け）。
    /// 会社情報＋支給年（和暦）＋提出先ごとの支払期間・報告人数・退職者人員（最大4件）＋受給者総人数を集計。
    /// </summary>
    public byte[] BuildPayReportSummaryPdf()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();

        CompanyInfo company;
        int 支給年;
        IReadOnlyList<PayReportSummaryBuilder.提出先集計> 提出先;
        int 総人数;
        using (var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser))
        {
            var conn = store.Connection;
            new CompanyRepository(conn).EnsureSchema();  // M_報告書提出先情報 等
            new YearEndRepository(conn).EnsureSchema();  // T_年末調整
            new EmployeeRepository(conn).EnsureSchema(); // T_従業員情報
            new PayDayRepository(conn).EnsureSchema();   // M_支給日情報（支給年）

            company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
            支給年 = new PayDayRepository(conn).Load().支給年;
            提出先 = PayReportSummaryBuilder.集計(conn);
            総人数 = PayReportSummaryBuilder.受給者総人数(conn);
        }

        var values = PayReportSummaryBuilder.Build(company, 支給年, 提出先, 総人数);
        return new PayReportSummaryReport().CreatePdf(values);
    }

    /// <summary>
    /// 法定調書の印刷ハブ（現行 F_法定調書印刷）。選択した帳票を現行と同じ並び
    /// （総括表→退職所得の源泉徴収票→報酬→不動産使用料→不動産譲受→不動産あっせん→法定調書合計表）で
    /// 生成し、1つの複数ページPDFへ結合する。各支払調書は登録済みの全件を出力し、
    /// <paramref name="teishutu"/>（1＝提出分のみ／2＝全て）で提出対象のみに絞る（総括表・合計表は全社集計のため対象外）。
    /// </summary>
    /// <param name="reports">選択帳票キー：soukatu/taishoku/houshu/fudousan/jouto/assen/goukei。</param>
    /// <param name="teishutu">1＝提出分のみ、2＝全て。</param>
    public byte[] BuildLegalDocumentsPdf(IReadOnlyList<string> reports, int teishutu)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<byte>();
        var sel = new HashSet<string>(reports, StringComparer.OrdinalIgnoreCase);
        var parts = new List<byte[]>();

        if (sel.Contains("soukatu")) parts.Add(BuildPayReportSummaryPdf());

        if (sel.Contains("taishoku"))
            foreach (int no in FilterRetirementNos(teishutu)) parts.Add(BuildRetirementWithholdingPdf(no));
        if (sel.Contains("houshu"))
            foreach (int no in FilterRewardNos(teishutu)) parts.Add(BuildRewardPdf(no));
        if (sel.Contains("fudousan"))
            foreach (int no in FilterRealEstateNos(teishutu)) parts.Add(BuildRealEstatePdf(no));
        if (sel.Contains("jouto"))
            foreach (int no in FilterRealEstateTransferNos(teishutu)) parts.Add(BuildRealEstateTransferPdf(no));
        if (sel.Contains("assen"))
            foreach (int no in FilterRealEstateMediationNos(teishutu)) parts.Add(BuildRealEstateMediationPdf(no));

        if (sel.Contains("goukei")) parts.Add(BuildLegalRecordSummaryPdf());

        return PdfMerger.Merge(parts);
    }

    // ---- 提出区分フィルタ：各支払調書の出力対象 整理番号を返す（現行 createReport の WHERE 相当） ----
    // 退職源泉・報酬：提出分のみ(1)＝列「提出」=true。不動産・譲受・あっせん：提出分のみ(1)＝列「不要」=true（＝提出対象）。
    // 全て(2)＝登録済み全件。

    private IReadOnlyList<int> FilterRetirementNos(int teishutu)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<int>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RetirementWithholdingRepository(store.Connection);
        repo.EnsureSchema();
        var nos = new List<int>();
        foreach (var item in repo.List())
            if (teishutu == 2 || repo.Load(item.No)?.提出 == true) nos.Add(item.No);
        return nos;
    }

    private IReadOnlyList<int> FilterRewardNos(int teishutu)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<int>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RewardRepository(store.Connection);
        repo.EnsureSchema();
        var nos = new List<int>();
        foreach (var item in repo.List())
            if (teishutu == 2 || repo.Load(item.No)?.提出 == true) nos.Add(item.No);
        return nos;
    }

    private IReadOnlyList<int> FilterRealEstateNos(int teishutu)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<int>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateRepository(store.Connection);
        repo.EnsureSchema();
        var nos = new List<int>();
        foreach (var item in repo.List())
            if (teishutu == 2 || repo.Load(item.No)?.不要 == true) nos.Add(item.No);
        return nos;
    }

    private IReadOnlyList<int> FilterRealEstateTransferNos(int teishutu)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<int>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateTransferRepository(store.Connection);
        repo.EnsureSchema();
        var nos = new List<int>();
        foreach (var item in repo.List())
            if (teishutu == 2 || repo.Load(item.No)?.不要 == true) nos.Add(item.No);
        return nos;
    }

    private IReadOnlyList<int> FilterRealEstateMediationNos(int teishutu)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<int>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var repo = new RealEstateMediationRepository(store.Connection);
        repo.EnsureSchema();
        var nos = new List<int>();
        foreach (var item in repo.List())
            if (teishutu == 2 || repo.Load(item.No)?.不要 == true) nos.Add(item.No);
        return nos;
    }

    // ===== バックアップ（現行 F_バックアップ設定 の移植） =====
    // 現行はデータベースファイルを暗号化コピーする。ブラウザ版のDBは SQLCipher で保存時暗号化済みのため、
    // バックアップは「同じ鍵で暗号化された一貫コピー」を VACUUM INTO で作成する（オンプレ・同一マシン復元用）。
    // 復号鍵は DataDir\keys 配下（DPAPI保護・マシン紐付き）に残るため、同一マシンでは .db を戻せば復元できる。

    /// <summary>バックアップの既定フォルダ（データフォルダ直下 backups）。</summary>
    private string 既定バックアップ先 => Path.Combine(DataDir, "backups");

    /// <summary>バックアップ設定（現行 M_自動バックアップ設定）。未登録なら既定フォルダ・自動オフ。</summary>
    public BackupSettings LoadBackupSettings()
    {
        if (!OperatingSystem.IsWindows()) return new BackupSettings(既定バックアップ先, false);
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        EnsureBackupSchema(store.Connection);
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "SELECT バックアップ先, 自動バックアップ有無 FROM M_自動バックアップ設定 LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new BackupSettings(既定バックアップ先, false);
        string 先 = r.IsDBNull(0) || r.GetString(0).Length == 0 ? 既定バックアップ先 : r.GetString(0);
        bool 自動 = !r.IsDBNull(1) && r.GetString(1) == "1";
        return new BackupSettings(先, 自動);
    }

    /// <summary>バックアップ設定を保存（現行 B_save＝全削除→1件INSERT）。フォルダは無ければ作成する。</summary>
    public BackupResult SaveBackupSettings(string dir, bool auto)
    {
        if (!OperatingSystem.IsWindows()) return new BackupResult(false, "Windows専用です。");
        if (string.IsNullOrWhiteSpace(dir)) return new BackupResult(false, "バックアップ先を入力してください。");
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) { AppErrors.Log(ex, "バックアップ先フォルダ作成エラー"); return new BackupResult(false, "バックアップ先フォルダを作成できません。詳細はログを確認してください。"); }

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        EnsureBackupSchema(store.Connection);
        using var tx = store.Connection.BeginTransaction();
        using (var del = store.Connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM M_自動バックアップ設定";
            del.ExecuteNonQuery();
        }
        using (var ins = store.Connection.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO M_自動バックアップ設定 (No, バックアップ先, 自動バックアップ有無) VALUES (1, $dir, $auto)";
            ins.Parameters.AddWithValue("$dir", dir);
            ins.Parameters.AddWithValue("$auto", auto ? "1" : "0");
            ins.ExecuteNonQuery();
        }
        tx.Commit();
        return new BackupResult(true, "設定を保存しました。");
    }

    /// <summary>
    /// 今すぐバックアップ（現行 B_bkupnow）。バックアップ先へ「yyyyMMddHHmmss_[コメント_]PayCalcDXPro.db」を作成する。
    /// SQLCipher の VACUUM INTO で、稼働中でも一貫した暗号化コピーを1ファイルで生成する。
    /// </summary>
    public BackupResult RunBackupNow(string dir, string? comment)
    {
        if (!OperatingSystem.IsWindows()) return new BackupResult(false, "Windows専用です。");
        if (string.IsNullOrWhiteSpace(dir)) return new BackupResult(false, "バックアップ先を入力してください。");

        // コメントはファイル名の一部になるため不正文字を弾く（現行 SB009E 相当）。
        if (!string.IsNullOrEmpty(comment) && comment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return new BackupResult(false, "コメントにファイル名として使えない文字が含まれています。");

        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) { AppErrors.Log(ex, "バックアップ先フォルダ作成エラー"); return new BackupResult(false, "バックアップ先フォルダを作成できません。詳細はログを確認してください。"); }

        string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string 名前 = stamp + "_" + (string.IsNullOrEmpty(comment) ? "" : comment + "_") + "PayCalcDXPro.db";
        string full = Path.Combine(dir, 名前);
        if (full.Length > 259) return new BackupResult(false, "保存先のパスが長すぎます。フォルダやコメントを短くしてください。");

        try
        {
            if (File.Exists(full)) File.Delete(full); // VACUUM INTO は既存ファイルへは失敗するため
            using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
            using var cmd = store.Connection.CreateCommand();
            // VACUUM INTO はパラメータ不可のため、パスを安全にエスケープして文字列リテラルにする。
            cmd.CommandText = $"VACUUM INTO '{full.Replace("'", "''")}'";
            cmd.ExecuteNonQuery();
            return new BackupResult(true, "バックアップしました。", full);
        }
        catch (UnauthorizedAccessException)
        {
            return new BackupResult(false, "バックアップ先フォルダへの書き込み権限がありません。");
        }
        catch (Exception ex)
        {
            AppErrors.Log(ex, "バックアップ失敗");
            return new BackupResult(false, "バックアップに失敗しました。詳細はログを確認してください。");
        }
    }

    /// <summary>自動バックアップが有効なら1回バックアップする（アプリ終了時に呼ぶ。現行の終了時自動バックアップ相当）。</summary>
    public void RunAutoBackupIfEnabled()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var s = LoadBackupSettings();
            if (s.自動 && !string.IsNullOrWhiteSpace(s.先)) RunBackupNow(s.先, "auto");
        }
        catch { /* 終了処理を妨げない */ }
    }

    /// <summary>
    /// サーバー（このPC）のフォルダ一覧を返す（フォルダ参照ダイアログ用）。
    /// path が空/存在しない場合はドライブ一覧のみ（CurrentPath 空）を返す。隠し/システムフォルダは除外。
    /// </summary>
    public DirListing ListDirectories(string? path)
    {
        if (!OperatingSystem.IsWindows()) return new DirListing("", null, Array.Empty<DirEntry>(), Array.Empty<string>());

        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToArray();

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return new DirListing("", null, Array.Empty<DirEntry>(), drives);

        var dir = new DirectoryInfo(path);
        var subs = new List<DirEntry>();
        try
        {
            foreach (var d in dir.GetDirectories())
            {
                if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                subs.Add(new DirEntry(d.Name, d.FullName));
            }
        }
        catch { /* アクセス拒否等は空一覧として扱う */ }
        subs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return new DirListing(dir.FullName, dir.Parent?.FullName, subs, drives);
    }

    /// <summary>指定フォルダ内に新しいフォルダを作成し、そのフルパスを返す（フォルダ参照ダイアログの「新規フォルダ」用）。</summary>
    public BackupResult CreateFolder(string parent, string name)
    {
        if (!OperatingSystem.IsWindows()) return new BackupResult(false, "Windows専用です。");
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            return new BackupResult(false, "作成先のフォルダが正しくありません。");
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return new BackupResult(false, "フォルダ名にファイル名として使えない文字が含まれています。");
        try
        {
            string full = Path.Combine(parent, name.Trim());
            Directory.CreateDirectory(full);
            return new BackupResult(true, "フォルダを作成しました。", full);
        }
        catch (Exception ex) { AppErrors.Log(ex, "フォルダ作成エラー"); return new BackupResult(false, "フォルダを作成できません。詳細はログを確認してください。"); }
    }

    /// <summary>暗号化DB本体のファイル名（SecureDataStore と同一）。</summary>
    private const string DbFileName = "PayCalcDXPro.db";

    /// <summary>
    /// 指定フォルダ内のバックアップファイル（*PayCalc*.db）を新しい順に一覧する（現行 取込時の *PayCalc*.db 検索）。
    /// </summary>
    public IReadOnlyList<BackupFileInfo> ListBackupFiles(string dir)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return Array.Empty<BackupFileInfo>();
        var list = new List<BackupFileInfo>();
        foreach (var f in Directory.GetFiles(dir, "*PayCalc*.db"))
        {
            var fi = new FileInfo(f);
            list.Add(new BackupFileInfo(fi.FullName, fi.Name, $"{fi.Length:#,0} バイト",
                fi.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss")));
        }
        // タイムスタンプ接頭辞のため、ファイル名の降順＝新しい順。
        list.Sort((a, b) => string.Compare(b.FileName, a.FileName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>
    /// 指定したバックアップファイルを削除する。誤削除防止のため、命名規則（*PayCalc*.db）に一致するファイルのみ削除を許可する。
    /// </summary>
    public BackupResult DeleteBackupFile(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return new BackupResult(false, "Windows専用です。");
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new BackupResult(false, "削除するバックアップファイルを選択してください。");
        var name = Path.GetFileName(filePath);
        if (name.IndexOf("PayCalc", StringComparison.OrdinalIgnoreCase) < 0
            || !name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            return new BackupResult(false, "バックアップファイル以外は削除できません。");
        try
        {
            File.Delete(filePath);
            return new BackupResult(true, $"バックアップを削除しました：{name}");
        }
        catch (Exception ex) { AppErrors.Log(ex, "バックアップ削除エラー"); return new BackupResult(false, "削除に失敗しました。詳細はログを確認してください。"); }
    }

    /// <summary>
    /// バックアップファイルから復元する（現行 btn_取込実行 の全体復元＝本年データ取込に相当）。
    /// 対象ファイルの形式を自動判別する：
    ///  ・ブラウザ版自身の SQLCipher バックアップ … 現在の鍵で開ける一貫コピーなので、現DBを退避して丸ごと置換する（最速）。
    ///  ・Windows版（デスクトップ）の AES 暗号バックアップ … 復号のうえ全テーブルを本年取込相当で置換する
    ///    （旧バージョンのバックアップも共通列コピーで取込可。マイナンバーは平文→GCM列暗号へ再暗号化）。
    /// いずれとしても開けない場合は現DBを一切変更しない。
    /// </summary>
    public BackupResult RestoreFromBackup(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return new BackupResult(false, "Windows専用です。");
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new BackupResult(false, "取込むバックアップファイルを選択してください。");

        byte[] dbKey;
        try { dbKey = DpapiKeyProtector.LoadOrCreate(Path.Combine(DataDir, "keys", "db.key"), 32, DataProtectionScope.CurrentUser); }
        catch (Exception ex) { AppErrors.Log(ex, "暗号鍵取得エラー"); return new BackupResult(false, "暗号鍵を取得できません。詳細はログを確認してください。"); }
        string 鍵 = Convert.ToBase64String(dbKey);

        var kind = DetectBackupKind(filePath, 鍵, out string? plainTemp);
        if (kind == BackupKind.Unknown)
            return new BackupResult(false, "このファイルはバックアップとして読み込めません（別のPCで作成された、対応していない、または破損している可能性があります）。復元を中止しました。");

        // Windows版（デスクトップ）バックアップ：全データを置換（本年取込相当。旧版・マイナンバー再暗号化に対応）。
        if (kind == BackupKind.LegacyWindows)
            return ImportLegacyBackup(plainTemp!, 全体: true, 退職者消去: false);

        // ブラウザ版自身のバックアップ：同一鍵で開ける一貫コピーなので丸ごと置換（最速・確実）。
        string dbPath = Path.Combine(DataDir, DbFileName);
        try
        {
            // プールされた接続がDBファイルを掴んだままだと置換できないため、全プールを閉じる。
            SqliteConnection.ClearAllPools();
            // ジャーナル残骸を掃除（既定はDELETEジャーナルのため通常は無い）。
            foreach (var ext in new[] { "-wal", "-shm", "-journal" })
                if (File.Exists(dbPath + ext)) File.Delete(dbPath + ext);
            // 現DBを退避（万一に備えたロールバック用）。
            if (File.Exists(dbPath)) File.Copy(dbPath, dbPath + ".prerestore", overwrite: true);
            // バックアップで丸ごと置換。
            File.Copy(filePath, dbPath, overwrite: true);
            return new BackupResult(true, "バックアップから復元しました。反映のため画面を再読み込みしてください。", dbPath);
        }
        catch (Exception ex)
        {
            AppErrors.Log(ex, "復元失敗");
            return new BackupResult(false, "復元に失敗しました。詳細はログを確認してください。");
        }
    }

    /// <summary>
    /// 前年データ取込（現行 F_データの初期化 の前年データ取込＝Opt_befor）。新年度を始める際、前年のバックアップから
    /// <b>マスタ項目のみ</b>（会社情報/部門/役職/銀行/報告書提出先/支給控除項目設定/従業員/マイナンバー）を現DBへ引き継ぐ。
    /// 給与/賞与/年末調整などの計算データは取込まない。住民税（特別徴収）は前年 7〜12月分を新年度 1〜5月分へ繰り越す。
    /// バックアップは現在のDBと同じ鍵で開けることを検証し、ATTACH してテーブル単位で置換する（全体復元のような丸ごと置換はしない）。
    /// </summary>
    /// <param name="filePath">取込むバックアップ（*.db）。</param>
    /// <param name="退職者消去">true=退職者（就退区分3/4）を取込まない、false=そのまま取り込む。</param>
    public BackupResult RestoreMastersFromBackup(string filePath, bool 退職者消去)
    {
        if (!OperatingSystem.IsWindows()) return new BackupResult(false, "Windows専用です。");
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new BackupResult(false, "取込むバックアップファイルを選択してください。");

        byte[] dbKey;
        try { dbKey = DpapiKeyProtector.LoadOrCreate(Path.Combine(DataDir, "keys", "db.key"), 32, DataProtectionScope.CurrentUser); }
        catch (Exception ex) { AppErrors.Log(ex, "暗号鍵取得エラー"); return new BackupResult(false, "暗号鍵を取得できません。詳細はログを確認してください。"); }
        string 鍵 = Convert.ToBase64String(dbKey);

        var kind = DetectBackupKind(filePath, 鍵, out string? plainTemp);
        if (kind == BackupKind.Unknown)
            return new BackupResult(false, "このファイルはバックアップとして読み込めません（別のPCで作成された、対応していない、または破損している可能性があります）。取込を中止しました。");

        // Windows版（デスクトップ）バックアップ：復号のうえマスタのみ取込（旧版・マイナンバー再暗号化に対応）。
        if (kind == BackupKind.LegacyWindows)
            return ImportLegacyBackup(plainTemp!, 全体: false, 退職者消去: 退職者消去);

        // ブラウザ版自身の SQLCipher バックアップ（従来経路）。
        try
        {
            using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
            var c = store.Connection;
            // ATTACH はトランザクション外で実行する（同じ鍵で開く）。
            ExecSql(c, null, $"ATTACH DATABASE '{filePath.Replace("'", "''")}' AS bk KEY '{鍵}'");
            try
            {
                using var tx = c.BeginTransaction();
                // マスタ（会社まわり・支給控除項目設定）は丸ごと置換。
                foreach (var t in new[] { "M_会社情報", "M_部門情報", "M_役職情報", "M_銀行情報", "M_報告書提出先情報", "M_支給控除項目設定" })
                    CopyAttachedTable(c, tx, t, null);
                // 従業員・マイナンバーは退職者の扱いで絞り込み（就退区分3=退職/4=死亡退職を除外）。
                CopyAttachedTable(c, tx, "T_従業員情報", 退職者消去 ? "就退区分 < 3" : null);
                CopyAttachedTable(c, tx, "T_マイナンバー情報",
                    退職者消去 ? "Number IN (SELECT Number FROM bk.\"T_従業員情報\" WHERE 就退区分 < 3)" : null);
                // 住民税（特別徴収）の年度繰越：前年 7〜12月分 → 新年度 1〜5月分。
                ExecSql(c, tx, "UPDATE main.\"T_従業員情報\" SET 住民税1_5月 = 住民税7_12月, 住民税6月 = NULL, 住民税7_12月 = NULL");
                tx.Commit();
            }
            finally { ExecSql(c, null, "DETACH DATABASE bk"); }

            Audit(store, $"前年データ（マスタ項目）を取込みました（退職者{(退職者消去 ? "消去" : "そのまま")}）");
            return new BackupResult(true, "前年データ（マスタ項目）を取込みました。反映のため画面を再読み込みしてください。");
        }
        catch (Exception ex)
        {
            AppErrors.Log(ex, "前年データ取込失敗");
            return new BackupResult(false, "前年データの取込に失敗しました。詳細はログを確認してください。");
        }
    }

    /// <summary>ATTACH した bk スキーマから main の同名テーブルへ、共通列のみをコピー（置換）する。</summary>
    private static void CopyAttachedTable(SqliteConnection c, SqliteTransaction tx, string table, string? whereClause)
    {
        var mainCols = AttachedColumns(c, "main", table);
        var bkCols = new HashSet<string>(AttachedColumns(c, "bk", table));
        var common = mainCols.Where(x => bkCols.Contains(x)).ToList();
        if (common.Count == 0) return; // バックアップに無いテーブルはスキップ
        string cols = string.Join(", ", common.Select(x => $"\"{x}\""));
        string where = string.IsNullOrEmpty(whereClause) ? "" : " WHERE " + whereClause;
        ExecSql(c, tx, $"DELETE FROM main.\"{table}\"");
        ExecSql(c, tx, $"INSERT INTO main.\"{table}\" ({cols}) SELECT {cols} FROM bk.\"{table}\"{where}");
    }

    private static List<string> AttachedColumns(SqliteConnection c, string schema, string table)
    {
        var cols = new List<string>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"PRAGMA {schema}.table_info(\"{table}\")";
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1)); // 1=name
        return cols;
    }

    private static void ExecSql(SqliteConnection c, SqliteTransaction? tx, string sql)
    {
        using var cmd = c.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ===== Windows版（デスクトップ）バックアップの取込 =====
    // 現行 F_データの初期化.btn_取込実行 の移植。移植元は「DB全体を AES-128-CBC でファイル暗号化した .db」を
    // バックアップとして出力し、旧バージョンのバックアップも取込めるよう設計されている（列名ベースの移行＋欠落列補完）。
    // ブラウザ版DBは移植元と同一スキーマ（日本語テーブル/列名）なので、復号のうえテーブル単位で共通列コピーすればよい。
    // ただしマイナンバーだけは、移植元＝平文列、ブラウザ版＝AES-256-GCM列暗号 と保存形式が異なるため、再暗号化して取込む。

    private enum BackupKind { Unknown, BrowserSqlCipher, LegacyWindows }

    /// <summary>
    /// バックアップファイルの形式を判別する。
    ///  ・ブラウザ版自身の SQLCipher バックアップ … 現在の鍵で開ける（<paramref name="plainTemp"/> は null）。
    ///  ・Windows版の AES 暗号バックアップ … "SyuhouSystem" で復号でき、平文SQLiteになる（<paramref name="plainTemp"/> に一時ファイルパス）。
    /// どちらでもなければ <see cref="BackupKind.Unknown"/>。呼び出し側は plainTemp の後始末（削除）を <see cref="ImportLegacyBackup"/> に委ねる。
    /// </summary>
    private static BackupKind DetectBackupKind(string filePath, string 鍵, out string? plainTemp)
    {
        plainTemp = null;

        // 1) ブラウザ版自身の SQLCipher バックアップか（現在の鍵で開けてテーブルがあるか）。
        try
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = filePath, Password = 鍵, Mode = SqliteOpenMode.ReadOnly };
            using var conn = new SqliteConnection(csb.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
            if (Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) > 0) return BackupKind.BrowserSqlCipher;
        }
        catch { /* SQLCipher として開けない → 次の判定へ */ }

        // 2) Windows版（デスクトップ）の AES 暗号バックアップか（既定パスワードで復号でき、SQLiteヘッダを持つか）。
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_import_{Guid.NewGuid():N}.db");
            LegacyDbDecryptor.DecryptFile(filePath, tmp); // 既定パスワード "SyuhouSystem"
            if (IsValidSqliteFile(tmp)) { plainTemp = tmp; return BackupKind.LegacyWindows; }
            TryDelete(tmp);
        }
        catch { /* 復号不可（別形式/破損） */ }

        return BackupKind.Unknown;
    }

    /// <summary>先頭16バイトが SQLite のマジック "SQLite format 3\0" かで平文SQLiteかを判定する。</summary>
    private static bool IsValidSqliteFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            byte[] header = new byte[16];
            if (fs.Read(header, 0, 16) < 16) return false;
            return System.Text.Encoding.ASCII.GetString(header) == "SQLite format 3\0";
        }
        catch { return false; }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 一時ファイル削除失敗は無視 */ }
    }

    /// <summary>
    /// 復号済み（平文SQLite）の Windows版バックアップを現DBへ取込む。<paramref name="全体"/>=true は本年取込相当（全テーブル置換）、
    /// false は前年データ取込（マスタのみ＋住民税繰越）。取込側のバージョンが現行より新しい場合は中止する。
    /// 呼び出し元（RestoreFromBackup／RestoreMastersFromBackup）で Windows 判定済みであることが前提。
    /// </summary>
    private BackupResult ImportLegacyBackup(string plainTemp, bool 全体, bool 退職者消去)
    {
        try
        {
            using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
            var c = store.Connection;

            // 取込先テーブルが無い（新規DB）と共通列コピーが空振りするため、対象スキーマを用意しておく。
            EnsureImportTargetSchemas(c);

            // 平文DBは空KEYで ATTACH（SQLCipher接続に平文DBを結合する標準手順）。
            ExecSql(c, null, $"ATTACH DATABASE '{plainTemp.Replace("'", "''")}' AS bk KEY ''");
            try
            {
                // 取込ファイルが現行より新しいバージョンなら中止（現行 btn_取込実行 のバージョンチェック）。
                string? verMsg = CheckLegacyVersion(c);
                if (verMsg is not null) return new BackupResult(false, verMsg);

                var protector = GetMyNumberProtector();
                using (var tx = c.BeginTransaction())
                {
                    // 会社まわりマスタ（丸ごと置換）。
                    foreach (var t in new[] { "M_会社情報", "M_部門情報", "M_役職情報", "M_銀行情報", "M_報告書提出先情報", "M_支給控除項目設定" })
                        CopyAttachedTable(c, tx, t, null);

                    if (全体)
                    {
                        // 本年取込：支給日・従業員・年末調整・各トランを全件そのまま置換。
                        CopyAttachedTable(c, tx, "M_支給日情報", null);
                        CopyAttachedTable(c, tx, "T_従業員情報", null);
                        CopyAttachedTable(c, tx, "T_年末調整", null);
                        foreach (var t in new[] { "T_給与トラン", "T_賞与トラン", "T_退職源泉情報", "T_年調報酬情報", "T_不動産情報", "T_不動産譲受", "T_不動産あっせん情報" })
                            CopyAttachedTable(c, tx, t, null);
                        // マイナンバー（平文→GCM列暗号へ再暗号化）。
                        ReencryptMyNumbersFromBk(c, tx, protector, null);
                    }
                    else
                    {
                        // 前年データ取込：従業員・マイナンバーのみ引き継ぎ、退職者は任意で除外。
                        CopyAttachedTable(c, tx, "T_従業員情報", 退職者消去 ? "就退区分 < 3" : null);
                        ReencryptMyNumbersFromBk(c, tx, protector,
                            退職者消去 ? "Number IN (SELECT Number FROM bk.\"T_従業員情報\" WHERE 就退区分 < 3)" : null);
                        // 住民税（特別徴収）の年度繰越：前年 7〜12月分 → 新年度 1〜5月分。
                        ExecSql(c, tx, "UPDATE main.\"T_従業員情報\" SET 住民税1_5月 = 住民税7_12月, 住民税6月 = NULL, 住民税7_12月 = NULL");
                    }
                    tx.Commit();
                }
            }
            finally { ExecSql(c, null, "DETACH DATABASE bk"); }

            Audit(store, "バックアップ取込", "更新",
                全体 ? "Windows版バックアップから全データを取込みました"
                     : $"Windows版バックアップから前年マスタを取込みました（退職者{(退職者消去 ? "消去" : "そのまま")}）");
            return new BackupResult(true, 全体
                ? "Windows版バックアップから全データを取込みました。反映のため画面を再読み込みしてください。"
                : "Windows版バックアップから前年データ（マスタ項目）を取込みました。反映のため画面を再読み込みしてください。");
        }
        catch (Exception ex)
        {
            AppErrors.Log(ex, "バックアップ取込失敗");
            return new BackupResult(false, "取込に失敗しました。詳細はログを確認してください。");
        }
        finally
        {
            TryDelete(plainTemp);
        }
    }

    /// <summary>取込先（main）の対象テーブルを用意する。移行済みDBなら既に存在するが、新規DBでも取込めるようにする。</summary>
    private static void EnsureImportTargetSchemas(SqliteConnection c)
    {
        new CompanyRepository(c).EnsureSchema();
        new PayItemRepository(c).EnsureSchema();
        new PayDayRepository(c).EnsureSchema();
        new EmployeeRepository(c).EnsureSchema();
        new PayrollTranRepository(c).EnsureSchema();
        new YearEndRepository(c).EnsureSchema();
        new BonusTranRepository(c).EnsureSchema();
        new RetirementWithholdingRepository(c).EnsureSchema();
        new RewardRepository(c).EnsureSchema();
        new RealEstateRepository(c).EnsureSchema();
        new RealEstateTransferRepository(c).EnsureSchema();
        new RealEstateMediationRepository(c).EnsureSchema();
    }

    /// <summary>
    /// bk（Windows版＝平文列）のマイナンバーを読み出し、ブラウザ版の AES-256-GCM 列暗号へ再暗号化して main へ格納する。
    /// bk に無い列（旧バージョン）は NULL として扱う。<paramref name="whereClause"/> は bk 側の絞り込み（退職者除外など）。
    /// </summary>
    private static void ReencryptMyNumbersFromBk(SqliteConnection c, SqliteTransaction tx, MyNumberProtector protector, string? whereClause)
    {
        // bk にマイナンバー表が無ければ何もしない。
        if (AttachedColumns(c, "bk", "T_マイナンバー情報").Count == 0) return;

        // main 側の表を用意（列暗号化した値を格納する）。
        ExecSql(c, tx,
            "CREATE TABLE IF NOT EXISTS \"T_マイナンバー情報\" (Number TEXT PRIMARY KEY, 本人 TEXT, 配偶者 TEXT, " +
            "扶養親族１ TEXT, 扶養親族２ TEXT, 扶養親族３ TEXT, 扶養親族４ TEXT, 扶養親族５ TEXT, 扶養親族６ TEXT, 扶養親族７ TEXT)");
        ExecSql(c, tx, "DELETE FROM main.\"T_マイナンバー情報\"");

        string[] 列 = { "本人", "配偶者", "扶養親族１", "扶養親族２", "扶養親族３", "扶養親族４", "扶養親族５", "扶養親族６", "扶養親族７" };
        var bkCols = new HashSet<string>(AttachedColumns(c, "bk", "T_マイナンバー情報"));
        string where = string.IsNullOrEmpty(whereClause) ? "" : " WHERE " + whereClause;

        // まず全行をメモリへ読み切る（同一接続でリーダーを開いたまま書き込めないため）。
        var rows = new List<(string number, string?[] vals)>();
        using (var read = c.CreateCommand())
        {
            read.Transaction = tx;
            string sel = "Number, " + string.Join(", ", 列.Select(x => bkCols.Contains(x) ? $"\"{x}\"" : $"NULL AS \"{x}\""));
            read.CommandText = $"SELECT {sel} FROM bk.\"T_マイナンバー情報\"{where}";
            using var r = read.ExecuteReader();
            while (r.Read())
            {
                string number = r.IsDBNull(0) ? "" : r.GetValue(0).ToString() ?? "";
                var vals = new string?[9];
                for (int i = 0; i < 9; i++) vals[i] = r.IsDBNull(i + 1) ? null : r.GetValue(i + 1).ToString();
                rows.Add((number, vals));
            }
        }

        // GCM で再暗号化して INSERT。
        foreach (var (number, vals) in rows)
        {
            if (string.IsNullOrEmpty(number)) continue;
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO main.\"T_マイナンバー情報\" (Number, 本人, 配偶者, 扶養親族１, 扶養親族２, 扶養親族３, 扶養親族４, 扶養親族５, 扶養親族６, 扶養親族７) " +
                "VALUES (@no, @v0, @v1, @v2, @v3, @v4, @v5, @v6, @v7, @v8)";
            ins.Parameters.AddWithValue("@no", number);
            for (int i = 0; i < 9; i++)
            {
                object val = string.IsNullOrEmpty(vals[i]) ? DBNull.Value : protector.Encrypt(vals[i]!);
                ins.Parameters.AddWithValue($"@v{i}", val);
            }
            ins.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 取込ファイル（bk）が現行DB（main）より新しいバージョンなら取込を拒否する（現行 M_バージョン管理 のメジャー/マイナー比較）。
    /// バージョン表が無い（十分に古い等）場合は比較不能として許可する。中止する場合はメッセージを返す。
    /// </summary>
    private static string? CheckLegacyVersion(SqliteConnection c)
    {
        var cur = ReadAssemblyVersion(c, "main");
        var bak = ReadAssemblyVersion(c, "bk");
        if (cur is null || bak is null) return null; // 比較不能 → 許可
        if (bak.Value.major > cur.Value.major ||
            (bak.Value.major == cur.Value.major && bak.Value.minor > cur.Value.minor))
            return $"取込むバックアップ（Ver {bak.Value.major}.{bak.Value.minor}）が現在のバージョン（Ver {cur.Value.major}.{cur.Value.minor}）より新しいため取込めません。先にアプリを最新へ更新してください。";
        return null;
    }

    private static (int major, int minor)? ReadAssemblyVersion(SqliteConnection c, string schema)
    {
        try
        {
            using (var chk = c.CreateCommand())
            {
                chk.CommandText = $"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE type='table' AND name='M_バージョン管理'";
                if (Convert.ToInt64(chk.ExecuteScalar() ?? 0L) == 0) return null;
            }
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT アセンブリバージョン FROM {schema}.\"M_バージョン管理\" LIMIT 1";
            string? v = cmd.ExecuteScalar()?.ToString();
            if (string.IsNullOrWhiteSpace(v)) return null;
            string[] parts = v.Split('.');
            int major = parts.Length > 0 && int.TryParse(parts[0], out var mj) ? mj : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out var mn) ? mn : 0;
            return (major, minor);
        }
        catch { return null; }
    }

    private static void EnsureBackupSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "M_自動バックアップ設定" (
                "No" INTEGER, "バックアップ先" TEXT, "自動バックアップ有無" TEXT, PRIMARY KEY("No")
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ===== データ初期化（現行 F_データの初期化 の各種データ削除／リセット部を移植） =====
    // いずれも依存データ（給与/賞与トラン・年末調整・支給日・従業員）の整合性を確認してから削除する。
    // 破壊的操作のため、UI側で確認ダイアログを出した上で呼ぶ。ブラウザ版に無いテーブル（T_定額減税・タイムカード）は対象外。

    /// <summary>会社情報関連マスタの初期化（現行 btn_会社情報）。支給日情報・従業員情報が残っていると不可。</summary>
    public InitResult Init会社情報()
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;
        if (TableCount(c, "M_支給日情報") != 0 || TableCount(c, "T_従業員情報") != 0)
            return new InitResult(false, "支給日情報または従業員情報が登録されているため、会社情報を初期化できません。先に従業員情報・支給日情報を初期化してください。");

        RunInTx(c,
            "DELETE FROM M_会社情報",
            "DELETE FROM M_部門情報",
            "DELETE FROM M_役職情報",
            "DELETE FROM M_銀行情報",
            "DELETE FROM M_報告書提出先情報",
            "DELETE FROM M_支給控除項目設定");
        Audit(store, "会社情報を初期化しました（法人番号/個人番号を含む）");
        return new InitResult(true, "会社情報を初期化しました。");
    }

    /// <summary>支給日情報の初期化（現行 btn_支給日情報）。給与/賞与/年末調整データが残っていると不可。</summary>
    public InitResult Init支給日情報()
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;
        if (計算年調データあり(c))
            return new InitResult(false, "給与・賞与・年末調整のデータが登録されているため初期化できません。先に給与計算データ・賞与計算データを初期化してください。");

        RunInTx(c,
            "UPDATE M_会社情報 SET 支給日登録フラグ = '0'",
            "DELETE FROM M_支給日情報");
        return new InitResult(true, "支給日情報を初期化しました。");
    }

    /// <summary>支給・控除項目設定の初期化（現行 btn_支給控除設定情報）。給与/賞与/年末調整データが残っていると不可。</summary>
    public InitResult Init支給控除項目設定()
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;
        if (計算年調データあり(c))
            return new InitResult(false, "給与・賞与・年末調整のデータが登録されているため初期化できません。先に給与計算データ・賞与計算データを初期化してください。");

        RunInTx(c,
            "DELETE FROM M_支給控除項目設定",
            "INSERT INTO M_支給控除項目設定 (レコード番号) VALUES (1)");
        return new InitResult(true, "支給・控除項目設定を初期化しました。");
    }

    /// <summary>従業員情報関連データの初期化（現行 btn_従業員情報）。給与/賞与/年末調整データが残っていると不可。</summary>
    public InitResult Init従業員情報()
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;
        if (TableCount(c, "T_給与トラン") != 0 || TableCount(c, "T_賞与トラン") != 0 || YearEndDataRowCount(c) != 0)
            return new InitResult(false, "給与・賞与・年末調整のデータが登録されているため、従業員情報を初期化できません。先に給与計算データ・賞与計算データ・年末調整を初期化してください。");

        RunInTx(c,
            "DELETE FROM T_従業員情報",
            "DELETE FROM T_年末調整",
            "DELETE FROM T_マイナンバー情報");
        Audit(store, "従業員情報を初期化しました（個人番号を含む）");
        return new InitResult(true, "従業員情報を初期化しました。");
    }

    /// <summary>
    /// 給与計算データの初期化（現行 btn_給与計算データ）。給与トランを削除し、支給日の処理済みフラグと年末調整の給与関連列をクリアする。
    /// <paramref name="支給回"/> を指定するとその1回分（＝1か月分）のみ、null なら全12回分を初期化する。
    /// </summary>
    public InitResult Init給与計算データ(int? 支給回 = null)
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;

        // 特定の支給回（月分）のみ：その回のトラン削除＋処理済みフラグ・年調集計列（給与/社保/源泉/扶養親族数）をクリア。
        if (支給回 is int k && k >= 1 && k <= 12)
        {
            string flag = $"UPDATE M_支給日情報 SET 処理済みフラグ{_全角[k - 1]} = '0'";
            string 年調 = $"UPDATE T_年末調整 SET 給与{k}月 = NULL, 社保{k}月 = NULL, 源泉{k}月 = NULL, 給与扶養親族数{k}月 = NULL";
            RunInTx(c, $"DELETE FROM T_給与トラン WHERE 支給回 = {k}", flag, 年調);
            return new InitResult(true, $"給与計算データ（第{k}回）を初期化しました。");
        }

        string 処理済みリセット = "UPDATE M_支給日情報 SET " +
            string.Join(", ", 全角1_12().Select(f => $"処理済みフラグ{f} = '0'"));
        var 年調列 = 月列("給与").Concat(月列("社保")).Concat(月列("源泉")).Concat(月列("給与扶養親族数"));
        string 年調クリア = "UPDATE T_年末調整 SET " + string.Join(", ", 年調列.Select(col => $"{col} = NULL"));

        RunInTx(c, "DELETE FROM T_給与トラン", 処理済みリセット, 年調クリア);
        return new InitResult(true, "給与計算データを初期化しました。");
    }

    /// <summary>
    /// 賞与計算データの初期化（現行 btn_賞与計算データ）。賞与トランを削除し、賞与処理済みフラグと年末調整の賞与関連列をクリアする。
    /// <paramref name="回"/> を指定するとその1回分のみ、null なら全4回分を初期化する。
    /// </summary>
    public InitResult Init賞与計算データ(int? 回 = null)
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;

        // 特定の回のみ：その回のトラン削除＋賞与処理済みフラグ・年調集計列（賞与/社保賞与/源泉賞与/扶養親族数）をクリア。
        if (回 is int k && k >= 1 && k <= 4)
        {
            string flag = $"UPDATE M_支給日情報 SET 賞与処理済みフラグ{_全角[k - 1]} = '0'";
            string 年調 = $"UPDATE T_年末調整 SET 賞与{k} = NULL, 社保賞与{k} = NULL, 源泉賞与{k} = NULL, 賞与扶養親族数{k}回目 = NULL";
            RunInTx(c, $"DELETE FROM T_賞与トラン WHERE 回 = {k}", flag, 年調);
            return new InitResult(true, $"賞与計算データ（第{k}回）を初期化しました。");
        }

        string 処理済みリセット = "UPDATE M_支給日情報 SET " +
            string.Join(", ", new[] { "１", "２", "３", "４" }.Select(f => $"賞与処理済みフラグ{f} = '0'"));
        var 年調列 = new[] { "賞与1", "賞与2", "賞与3", "賞与4",
            "社保賞与1", "社保賞与2", "社保賞与3", "社保賞与4",
            "源泉賞与1", "源泉賞与2", "源泉賞与3", "源泉賞与4",
            "賞与扶養親族数1回目", "賞与扶養親族数2回目", "賞与扶養親族数3回目", "賞与扶養親族数4回目" };
        string 年調クリア = "UPDATE T_年末調整 SET " + string.Join(", ", 年調列.Select(col => $"{col} = NULL"));

        RunInTx(c, "DELETE FROM T_賞与トラン", 処理済みリセット, 年調クリア);
        return new InitResult(true, "賞与計算データを初期化しました。");
    }

    /// <summary>アクセスログ（操作ログ）の初期化（現行 btn_アクセスログデータ）。</summary>
    public InitResult Initアクセスログ()
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        RunInTx(store.Connection, "DELETE FROM T_操作ログ");
        return new InitResult(true, "アクセスログを初期化しました。");
    }

    // 年末調整データ初期化で削除対象となる支払調書テーブル（キー→テーブル名）。
    private static readonly (string Key, string Label, string Table)[] _支払調書テーブル =
    {
        ("taishoku", "退職所得の源泉徴収票・特別徴収票", "T_退職源泉情報"),
        ("houshu",   "報酬、料金、契約金及び賞金の支払調書", "T_年調報酬情報"),
        ("fudosan",  "不動産の使用料等の支払調書", "T_不動産情報"),
        ("jouto",    "不動産等の譲受けの対価の支払調書", "T_不動産譲受"),
        ("assen",    "不動産等の売買又は貸付けのあっせん手数料の支払調書", "T_不動産あっせん情報"),
    };

    /// <summary>年末調整データ初期化画面の対象一覧（各支払調書テーブルのキー/ラベル/データ有無）。データが無い/未作成のものは初期化対象に選べない。</summary>
    public IReadOnlyList<YearEndInitTarget> YearEnd支払調書対象()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<YearEndInitTarget>();
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;
        return _支払調書テーブル
            .Select(t => new YearEndInitTarget(t.Key, t.Label, TableCountSafe(c, t.Table) > 0))
            .ToArray();
    }

    /// <summary>
    /// 年末調整データの初期化（現行 F_法定調書の初期化選択）。
    /// T_年末調整の主キー(Number)以外を全てNULLにし、選択された支払調書テーブルを削除する。
    /// マイナンバーは削除しない（配偶者・扶養親族のマイナンバーは従業員情報の初期化でのみ削除）。ブラウザ版に無い T_定額減税 は対象外。
    /// </summary>
    /// <param name="削除する支払調書キー"><see cref="YearEnd支払調書対象"/> のキーのうち削除するもの。</param>
    public InitResult Init年末調整データ(IReadOnlyCollection<string> 削除する支払調書キー)
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;

        var stmts = new List<string>();

        // T_年末調整：年末調整の計算結果・集計列のみNULL（現行 nmtSql と同じ列）。
        // 配偶者・扶養親族の情報（配偶者/フリガナ/生年月日/扶養親族名前1〜7/続柄/障害/同居/非居住者/収入 等）は残す。
        var 計算結果クリア列 = new[]
        {
            "方法", "課税支給額", "社会保険料", "小規模共済", "生命保険料控除", "地震保険料控除",
            "配偶者特別控除", "配偶者控除", "扶養控除", "基礎控除", "障害者控除", "寡婦控除", "勤労学生控除",
            "住宅借入金控除", "差引課税給与所得金額", "算出年税額", "住宅借入金等特別控除", "年調年税額",
            "差引超過額不足額", "本年最後給与充当額", "未払給与充当額", "差引還付金額", "本年還付金額",
            "翌年還付金額", "翌年徴収金額", "同一生計配偶者_有", "同一生計配偶者_従有",
            "控除対象扶養親族_特定人数", "控除対象扶養親族_老人人数", "控除対象扶養親族_その他人数",
            "控除対象扶養親族_特定従人数", "控除対象扶養親族_老人従人数", "控除対象扶養親族_その他従人数",
            "非居住者である親族数", "勤労学生", "障害者_一般人数", "障害者_特別人数", "同居特別障害者人数",
            "控除対象扶養親族_人数", "１６歳未満扶養親族_人数", "控除対象配偶者", "老人控除対象配偶者",
        };
        var 年調実在列 = new HashSet<string>(NonKeyColumns(c, "T_年末調整", "Number"), StringComparer.OrdinalIgnoreCase);
        var 年調クリア列 = 計算結果クリア列.Where(年調実在列.Contains).ToArray();
        if (年調クリア列.Length > 0)
            stmts.Add("UPDATE T_年末調整 SET " + string.Join(", ", 年調クリア列.Select(n => $"\"{n}\" = NULL")));

        // マイナンバー（本人・配偶者・扶養親族）は年末調整初期化では削除しない（ユーザー仕様）。
        // 配偶者・扶養親族のマイナンバーは「従業員情報の初期化」（Init従業員情報 の T_マイナンバー情報 削除）でのみ削除する。

        // 選択された支払調書テーブルを削除（存在するものだけ）。
        int 削除数 = 0;
        foreach (var (key, _, table) in _支払調書テーブル)
        {
            if (削除する支払調書キー.Contains(key) && TableCountSafe(c, table) > 0)
            {
                stmts.Add($"DELETE FROM {table}");
                削除数++;
            }
        }

        RunInTx(c, stmts.ToArray());
        Audit(store, $"年末調整データを初期化しました（支払調書 {削除数} 件を削除）");
        return new InitResult(true, "年末調整データを初期化しました。");
    }

    // 全データ初期化で削除する（＝空にする）テーブル一覧。依存を考慮した順（トラン→年調→従業員→支払調書→マスタ→ログ）。
    // セキュリティアカウント設定・自動バックアップ設定・バージョン管理は対象外（システム/設定のため残す）。
    private static readonly string[] _全初期化削除テーブル =
    {
        "T_給与トラン", "T_賞与トラン",
        "T_年末調整", "T_マイナンバー情報", "T_従業員情報",
        "T_退職源泉情報", "T_年調報酬情報", "T_不動産情報", "T_不動産譲受", "T_不動産あっせん情報",
        "M_支給日情報",
        "M_会社情報", "M_部門情報", "M_役職情報", "M_銀行情報", "M_報告書提出先情報",
        "M_支給控除項目設定",
        "T_操作ログ",
    };

    /// <summary>
    /// 全データの初期化。登録された業務データ（会社情報・従業員・給与／賞与・年末調整・各支払調書・操作ログ 等）を
    /// すべて削除し、初期状態に戻す。個別初期化のような依存ガードは行わず一括削除する（すべて消すため）。
    /// セキュリティアカウント設定・バックアップ設定は残す。
    /// </summary>
    public InitResult Init全データ()
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var c = store.Connection;

        var stmts = new List<string>();
        foreach (var t in _全初期化削除テーブル)
            if (TableExists(c, t)) stmts.Add($"DELETE FROM {t}");
        // 支給・控除項目設定は既定行を1件戻す（個別の Init支給控除項目設定 と同じ）。
        if (TableExists(c, "M_支給控除項目設定"))
            stmts.Add("INSERT INTO M_支給控除項目設定 (レコード番号) VALUES (1)");

        RunInTx(c, stmts.ToArray());
        Audit(store, "全データを初期化しました（会社情報・従業員・給与／賞与・年末調整・各支払調書・操作ログ）");
        return new InitResult(true, "全データを初期化しました。");
    }

    // ===== セキュリティアカウント設定（現行 F_データの初期化 の設定/解除部を移植） =====
    // 現行は平文保存だが、ブラウザ版は SqlCipherUserStore（PBKDF2ハッシュ）で保存する。単一アカウント運用。
    // アカウントが登録されている場合、Program.cs のゲートミドルウェアが未認証アクセスを /login へ誘導する。

    /// <summary>セキュリティアカウントが登録されているか（＝ログインゲートを有効にするか）。</summary>
    public bool SecurityAccountExists()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        return store.Users.Count() > 0;
    }

    /// <summary>登録済みアカウントの ID／設定者名（未登録なら null）。パスワードはハッシュのため取得しない。</summary>
    public SecurityAccountInfo? GetSecurityAccount()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        var a = store.Users.GetSingle();
        return a is null ? null : new SecurityAccountInfo(a.Value.Id, a.Value.Name);
    }

    /// <summary>セキュリティアカウントを設定（現行 btn_設定）。単一アカウント運用のため既存を消してから登録。ロールは管理者。</summary>
    public InitResult SetSecurityAccount(string id, string name, string password)
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        if (string.IsNullOrWhiteSpace(id)) return new InitResult(false, "アカウントIDを入力してください。");
        if (string.IsNullOrWhiteSpace(password)) return new InitResult(false, "パスワードを入力してください。");
        if (string.IsNullOrWhiteSpace(name)) return new InitResult(false, "設定者名を入力してください。");

        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        store.Users.DeleteAll();
        store.Users.CreateOrUpdate(id.Trim(), name.Trim(), password, Role.管理者);
        Audit(store, "セキュリティアカウント", "更新", $"セキュリティアカウントを設定しました（アカウントID：{id.Trim()}）");
        return new InitResult(true, "セキュリティアカウントを設定しました。");
    }

    /// <summary>セキュリティアカウントを解除（現行 btn_解除）。全アカウントを削除しログインゲートを無効化する。</summary>
    public InitResult ClearSecurityAccount()
    {
        if (!OperatingSystem.IsWindows()) return new InitResult(false, "Windows専用です。");
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        store.Users.DeleteAll();
        Audit(store, "セキュリティアカウント", "解除", "セキュリティアカウントを解除しました");
        return new InitResult(true, "セキュリティアカウントを解除しました。");
    }

    /// <summary>ログイン認証。ID／パスワードを検証して認証済み操作者を返す（失敗なら null）。</summary>
    public Principal? AuthenticateSecurityAccount(string id, string password)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var store = new SecureDataStore(DataDir, DataProtectionScope.CurrentUser);
        return new AuthenticationService(store.Users).Authenticate(id, password);
    }

    // ---- データ初期化の内部ヘルパ ----

    private static readonly string[] _全角 = { "１", "２", "３", "４", "５", "６", "７", "８", "９", "１０", "１１", "１２" };
    private static IEnumerable<string> 全角1_12() => _全角;
    private static IEnumerable<string> 月列(string prefix) => Enumerable.Range(1, 12).Select(n => $"{prefix}{n}月");

    /// <summary>給与/賞与トラン・年末調整のいずれかにデータがあるか（支給日・支給控除の初期化ガード）。</summary>
    private bool 計算年調データあり(SqliteConnection c)
        => TableCount(c, "T_給与トラン") != 0 || TableCount(c, "T_賞与トラン") != 0 || TableCount(c, "T_年末調整") != 0;

    private static long TableCount(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    /// <summary><see cref="TableCount"/> と同じだが、テーブルが未作成の場合は 0 を返す。</summary>
    private static long TableCountSafe(SqliteConnection c, string table)
    {
        try { return TableCount(c, table); }
        catch { return 0; }
    }

    /// <summary>指定テーブルが存在するか（sqlite_master 参照）。</summary>
    private static bool TableExists(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1";
        cmd.Parameters.AddWithValue("@n", table);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>指定テーブルの主キー以外かつ <paramref name="exclude"/> 以外の列名一覧（PRAGMA table_info で動的取得）。</summary>
    private static List<string> NonKeyColumns(SqliteConnection c, string table, string exclude)
    {
        var cols = new List<string>();
        using var pragma = c.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var r = pragma.ExecuteReader();
        while (r.Read())
        {
            string name = r.GetString(1);   // name
            long pk = r.GetInt64(5);         // pk
            if (pk == 0 && !name.Equals(exclude, StringComparison.OrdinalIgnoreCase)) cols.Add(name);
        }
        return cols;
    }

    /// <summary>T_年末調整で主キー(Number)以外に非NULL値を持つ行数（現行 getNnmtRecodeCount 相当・列は動的に取得）。</summary>
    private static long YearEndDataRowCount(SqliteConnection c)
    {
        var cols = NonKeyColumns(c, "T_年末調整", "Number");
        if (cols.Count == 0) return 0;
        string where = string.Join(" OR ", cols.Select(n => $"\"{n}\" IS NOT NULL"));
        using var cnt = c.CreateCommand();
        cnt.CommandText = $"SELECT COUNT(*) FROM T_年末調整 WHERE {where}";
        return Convert.ToInt64(cnt.ExecuteScalar() ?? 0L);
    }

    private static void RunInTx(SqliteConnection c, params string[] statements)
    {
        using var tx = c.BeginTransaction();
        foreach (var sql in statements)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>データ初期化の操作ログを T_操作ログ に直接記録する（現行 AccessLog.put 相当）。監査失敗は本処理を妨げない。</summary>
    private void Audit(SecureDataStore store, string detail)
        => Audit(store, "データの初期化", "消去", detail);

    /// <summary>操作ログを T_操作ログ に直接記録する（処理／取扱を指定可能）。操作者は現在ログイン中の実ユーザー（未ログイン時はシステム）。監査失敗は本処理を妨げない。</summary>
    private void Audit(SecureDataStore store, string 処理, string 取扱, string detail)
    {
        try
        {
            var op = 操作者;
            store.Audit.EnsureSchema();
            using var cmd = store.Connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO T_操作ログ (日時, アカウントID, 操作者, 処理, 取扱, 詳細) VALUES (@d, @id, @name, @proc, @kind, @detail)";
            cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff"));
            cmd.Parameters.AddWithValue("@id", op.Id);
            cmd.Parameters.AddWithValue("@name", op.Name);
            cmd.Parameters.AddWithValue("@proc", 処理);
            cmd.Parameters.AddWithValue("@kind", 取扱);
            cmd.Parameters.AddWithValue("@detail", detail);
            cmd.ExecuteNonQuery();
        }
        catch { /* 監査失敗は本処理を妨げない */ }
    }
}

/// <summary>データ初期化操作の結果（成否・メッセージ）。</summary>
public sealed record InitResult(bool Ok, string Message);

/// <summary>年末調整データ初期化で削除できる支払調書テーブルの一項目（キー・表示名・データ有無）。</summary>
public sealed record YearEndInitTarget(string Key, string Label, bool HasData);

/// <summary>登録済みセキュリティアカウントの表示情報（アカウントID・設定者名）。パスワードは含まない。</summary>
public sealed record SecurityAccountInfo(string Id, string Name);

/// <summary>バックアップ設定（バックアップ先フォルダ・自動バックアップ有無）。</summary>
public sealed record BackupSettings(string 先, bool 自動);

/// <summary>バックアップ操作の結果（成否・メッセージ・作成したファイルのフルパス）。</summary>
public sealed record BackupResult(bool Ok, string Message, string? Path = null);

/// <summary>バックアップファイル1件（フルパス・ファイル名・サイズ表示・更新日時表示）。復元の選択一覧用。</summary>
public sealed record BackupFileInfo(string FullPath, string FileName, string SizeText, string DateText);

/// <summary>フォルダ参照ダイアログの1フォルダ（表示名・フルパス）。</summary>
public sealed record DirEntry(string Name, string FullPath);

/// <summary>フォルダ参照ダイアログ用のフォルダ一覧（現在パス・親パス・サブフォルダ・ドライブ一覧）。</summary>
public sealed record DirListing(
    string CurrentPath, string? ParentPath, IReadOnlyList<DirEntry> Directories, IReadOnlyList<string> Drives);
