using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Payroll;

namespace PayCalcDXPro.Web;

/// <summary>
/// タイムカード計算データ(CSV)の取込を担う共通モジュール。
/// 転記ファイルの格納パス設定・CSV検出・新旧フォーマットの自動判別＆解析・給与トランへの反映・取込ログを集約する。
/// DB接続や給与計算ロジック（LoadPayrollMeta / ComputeOvertime / CalculatePayroll など）は
/// <see cref="LocalDatabaseService"/> の公開APIへ委譲する。Windows専用。
/// </summary>
public sealed class TimeCardImportService
{
    private readonly LocalDatabaseService _db;

    public TimeCardImportService(LocalDatabaseService db) => _db = db;

    // ===== タイムカード計算EXCELの転記情報（現行 F_データの初期化 の groupBox8「タイムカード計算EXCELからの転記情報」を移植） =====
    // 給与計算の処理月選択時に、ここで設定したフォルダから該当ファイルを転記する（転記処理自体は別スコープ）。

    private static void EnsureTimeCardSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "M_タイムカード計算" (
                "No" INTEGER, "転記ファイルパス" TEXT, PRIMARY KEY("No")
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>タイムカード転記ファイルの格納パス設定（現行 M_タイムカード計算）。未登録なら空。</summary>
    public TimeCardSetting LoadTimeCardSetting()
    {
        if (!OperatingSystem.IsWindows()) return new TimeCardSetting("");
        using var store = new SecureDataStore(_db.DataDir, DataProtectionScope.CurrentUser);
        EnsureTimeCardSchema(store.Connection);
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "SELECT 転記ファイルパス FROM M_タイムカード計算 LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new TimeCardSetting("");
        return new TimeCardSetting(r.IsDBNull(0) ? "" : r.GetString(0));
    }

    /// <summary>タイムカード転記ファイルの格納パスを保存（現行 btn_転記設定＝全削除→1件INSERT）。必須・フォルダ存在をチェックする。</summary>
    public BackupResult SaveTimeCardSetting(string dir)
    {
        if (!OperatingSystem.IsWindows()) return new BackupResult(false, "Windows専用です。");
        // 現行の必須チェック（SB020E）・存在チェック（SB019E）を踏襲。バックアップ先と違いフォルダは作成しない。
        if (string.IsNullOrWhiteSpace(dir)) return new BackupResult(false, "転記ファイルの格納パスが未入力です。");
        if (!Directory.Exists(dir)) return new BackupResult(false, "転記ファイルの格納先が存在しません。");

        using var store = new SecureDataStore(_db.DataDir, DataProtectionScope.CurrentUser);
        EnsureTimeCardSchema(store.Connection);
        using var tx = store.Connection.BeginTransaction();
        using (var del = store.Connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM M_タイムカード計算";
            del.ExecuteNonQuery();
        }
        using (var ins = store.Connection.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO M_タイムカード計算 (No, 転記ファイルパス) VALUES (1, $dir)";
            ins.Parameters.AddWithValue("$dir", dir);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
        return new BackupResult(true, "設定しました。");
    }

    // ===== タイムカードCSV取込（新旧フォーマット自動判別）=====
    // 旧: <支給月>_TmCalc.csv（7列）＝基本給・出勤日数・労働時間のみ転記（移植元 F_処理月選択.timeCardCalc）。
    // 新: kyuyo_<年>_<月>.csv（32列）＝勤怠・残業をフル転記。残業金額列(16-23)は検算表示のみ（時間から再計算）。
    // 判別はデータ1行目の列数（7=旧／32=新）で行う。先頭4列（年・月・従業員NO・氏名）は新旧共通。

    /// <summary>
    /// タイムカードCSVの列インデックス定義。列位置のマジックナンバーをここに集約する。
    /// 先頭7列（年・月・従業員NO・氏名・基本給・出勤日数・労働時間）は新旧フォーマットで同位置。
    /// 8列目以降は新フォーマット(kyuyo・32列)のみ。旧フォーマット(_TmCalc)は7列で完結する。
    /// </summary>
    private static class TcCol
    {
        // --- 新旧共通（0〜6） ---
        public const int 年 = 0;
        public const int 月 = 1;
        public const int 従業員No = 2;
        public const int 氏名 = 3;
        public const int 基本給 = 4;
        public const int 出勤日数 = 5;   // 旧＝出勤日数／新＝労働日数
        public const int 労働時間 = 6;

        // --- 新フォーマット(kyuyo・32列)のみ ---
        public const int 所定内残業 = 8;
        public const int 法定外残業 = 9;
        public const int 深夜残業 = 10;
        public const int 法定休日残業 = 11;
        public const int 休日深夜 = 12;
        public const int 週40h超 = 13;
        public const int 月60h超残業 = 14;
        public const int 月60h超深夜 = 15;
        public const int 残業金額_開始 = 16;   // 16〜23の8列合計を検算に使う
        public const int 残業金額_終了 = 23;
        public const int 通常出勤 = 24;
        public const int 有給休暇 = 26;
        public const int 振休 = 27;
        public const int 代休 = 28;
        public const int 遅刻 = 29;
        public const int 早退 = 30;
        public const int 欠勤 = 31;
    }

    private static void EnsureTimeCardLogSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""M_タイムカード取込ログ"" (
            ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT, ""取込日時"" TEXT, ""対象年月"" TEXT,
            ""ファイル名"" TEXT, ""フォーマット"" TEXT, ""反映件数"" INTEGER, ""スキップ件数"" INTEGER);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>h.mm 形式（小数部が分）の時間を分繰り上げで加算する。週40h超残業を普通残業へ合算する等に使う。</summary>
    private static double? AddHmm(double? a, double? b)
    {
        if (a is null && b is null) return null;
        static int Mins(double? x)
        {
            if (x is null) return 0;
            int h = (int)Math.Floor(x.Value);
            int m = (int)Math.Round((x.Value - h) * 100);
            return h * 60 + m;
        }
        int t = Mins(a) + Mins(b);
        return t / 60 + (t % 60) / 100.0;
    }

    /// <summary>日数（10進）の加算。遅刻＋早退・代休＋振休の合算に使う。</summary>
    private static double? AddDays(double? a, double? b)
        => (a is null && b is null) ? null : (a ?? 0) + (b ?? 0);

    /// <summary>
    /// 時間外手当の内訳（CSV残業金額列16〜23）と、給料明細 備考欄へ表示する略称の対応。
    /// 採用した時間外手当(=CSV残業金額合計)の内訳を「略称＋金額」で備考へ書き出すために使う。
    /// </summary>
    private static readonly (int 列, string 略称)[] Tc残業金額内訳 =
    {
        (TcCol.残業金額_開始 + 0, "所定内"),  // 所定内残業（金額）
        (TcCol.残業金額_開始 + 1, "法外"),    // 法定外残業（金額）
        (TcCol.残業金額_開始 + 2, "深夜"),    // 深夜残業（金額）
        (TcCol.残業金額_開始 + 3, "法休"),    // 法定休日残業（金額）
        (TcCol.残業金額_開始 + 4, "休深"),    // 休日深夜残業（金額）
        (TcCol.残業金額_開始 + 5, "週40超"),  // 週40h超残業（金額）
        (TcCol.残業金額_開始 + 6, "月60超"),  // 月60h超残業（金額）
        (TcCol.残業金額_開始 + 7, "月60深"),  // 月60h超深夜残業（金額）
    };

    /// <summary>
    /// タイムカードCSV1列（1項目）→給与トラン(PayrollEntry)項目の対応定義。
    /// 「どのCSV列を（どんな変換で）どの項目へ入れるか」を1箇所のテーブルに集約し、
    /// 解析(AnalyzeTimeCardCsv=抽出)と反映(ApplyTimeCardImport=空欄維持コピー)の両方が同じ定義を回す。
    /// </summary>
    /// <param name="名称">ログ・保守用の項目名。</param>
    /// <param name="新のみ">true＝新フォーマット(kyuyo・32列)専用。旧(_TmCalc・7列)では対象外。</param>
    /// <param name="抽出">CSV行から値を取り出す。引数はセル取得アクセサ（D=double／I=int）。変換(AddHmm/AddDays等)もここで行う。</param>
    /// <param name="取得">転記用PayrollEntryから当項目値を読む（反映時のコピー元）。</param>
    /// <param name="設定">PayrollEntryへ当項目値を書く。基本給のようにintへ丸める項目もここで吸収する。</param>
    private sealed record TcField(
        string 名称,
        bool 新のみ,
        Func<Func<int, double?>, Func<int, int?>, double?> 抽出,
        Func<PayrollEntry, double?> 取得,
        Action<PayrollEntry, double?> 設定);

    /// <summary>
    /// タイムカードCSV→給与トランのフィールドマッピング定義（テーブル方式）。
    /// 先頭3項目（基本給・出勤日数・労働時間）は新旧共通、以降は新フォーマット専用。
    /// </summary>
    private static readonly TcField[] TcFields =
    {
        // --- 新旧共通 ---
        new("基本給",   false, (D, I) => I(TcCol.基本給),   e => e.基本給,   (e, x) => e.基本給   = x is null ? (int?)null : (int)x.Value),
        new("出勤日数", false, (D, I) => D(TcCol.出勤日数), e => e.出勤日数, (e, x) => e.出勤日数 = x),
        new("労働時間", false, (D, I) => D(TcCol.労働時間), e => e.労働時間, (e, x) => e.労働時間 = x),

        // --- 新フォーマット(kyuyo・32列)専用 ---
        new("所定出勤日数",   true, (D, I) => D(TcCol.通常出勤),   e => e.所定出勤日数,   (e, x) => e.所定出勤日数   = x),  // 通常
        new("法定内残業",     true, (D, I) => D(TcCol.所定内残業), e => e.法定内残業,     (e, x) => e.法定内残業     = x),  // 所定内残業
        new("普通残業60h以下", true, (D, I) => AddHmm(D(TcCol.法定外残業), D(TcCol.週40h超)), e => e.普通残業60h以下, (e, x) => e.普通残業60h以下 = x), // 法定外残業＋週40h超
        new("深夜残業60h以下", true, (D, I) => D(TcCol.深夜残業),   e => e.深夜残業60h以下, (e, x) => e.深夜残業60h以下 = x),
        new("休日出勤60h以下", true, (D, I) => D(TcCol.法定休日残業), e => e.休日出勤60h以下, (e, x) => e.休日出勤60h以下 = x), // 法定休日残業
        new("休日深夜60h以下", true, (D, I) => D(TcCol.休日深夜),   e => e.休日深夜60h以下, (e, x) => e.休日深夜60h以下 = x),
        new("普通残業60h超",   true, (D, I) => D(TcCol.月60h超残業), e => e.普通残業60h超,   (e, x) => e.普通残業60h超   = x), // 月60h超残業
        new("深夜残業60h超",   true, (D, I) => D(TcCol.月60h超深夜), e => e.深夜残業60h超,   (e, x) => e.深夜残業60h超   = x), // 月60h超深夜
        new("有給休暇",       true, (D, I) => D(TcCol.有給休暇),   e => e.有給休暇,       (e, x) => e.有給休暇       = x),
        new("代休",           true, (D, I) => AddDays(D(TcCol.代休), D(TcCol.振休)), e => e.代休, (e, x) => e.代休 = x), // 代休＋振休
        new("遅刻早退",       true, (D, I) => AddDays(D(TcCol.遅刻), D(TcCol.早退)), e => e.遅刻早退, (e, x) => e.遅刻早退 = x), // 遅刻＋早退
        new("欠勤日数_一般",  true, (D, I) => D(TcCol.欠勤),       e => e.欠勤日数_一般,  (e, x) => e.欠勤日数_一般  = x),
    };

    /// <summary>指定支給回の対象年月に対応するタイムカードCSVを格納フォルダから探す（新→旧の順）。無ければ null。</summary>
    public string? FindTimeCardCsv(int 支給回)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var dir = LoadTimeCardSetting().格納パス;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;
        var meta = _db.LoadPayrollMeta(支給回);
        if (int.TryParse(meta.対象年, out int y) && int.TryParse(meta.対象月, out int mo))
        {
            var newPath = Path.Combine(dir, $"kyuyo_{y}_{mo:00}.csv");
            if (File.Exists(newPath)) return newPath;
        }
        // 旧フォーマット：*_TmCalc.csv のうち、中身の年月が対象年月に一致するもの。
        foreach (var f in Directory.GetFiles(dir, "*_TmCalc.csv"))
        {
            try
            {
                var row = File.ReadLines(f, System.Text.Encoding.UTF8).Skip(1).FirstOrDefault()?.Split(',');
                if (row is { Length: >= 2 } && row[TcCol.年].Trim() == meta.対象年 && row[TcCol.月].Trim() == meta.対象月)
                    return f;
            }
            catch { /* 読めないファイルは無視 */ }
        }
        return null;
    }

    /// <summary>タイムカードCSVを解析し、新旧を自動判別して取込プレビュー計画を作る（DBは変更しない）。</summary>
    public TimeCardImportPlan AnalyzeTimeCardCsv(string filePath, int 支給回)
    {
        TimeCardImportPlan Fail(string m) => new(false, m, filePath, Path.GetFileName(filePath),
            TimeCardCsvFormat.新_Kyuyo, 支給回, "", false, Array.Empty<TimeCardImportRow>());
        if (!OperatingSystem.IsWindows()) return Fail("Windows専用です。");
        if (!File.Exists(filePath)) return Fail("ファイルが見つかりません。");

        string[] lines;
        try { lines = File.ReadAllLines(filePath, System.Text.Encoding.UTF8); }
        catch (Exception ex) { AppErrors.Log(ex, "タイムカードファイル読込エラー"); return Fail("ファイルを読めません。ファイル形式をご確認ください。"); }
        if (lines.Length < 2) return Fail("データ行がありません。");

        var meta = _db.LoadPayrollMeta(支給回);
        string 対象年月 = $"{meta.対象年}/{meta.対象月}";
        var byNo = meta.従業員.GroupBy(e => (e.No ?? "").Trim())
                              .ToDictionary(g => g.Key, g => g.First());

        // フォーマット判定：データ1行目の列数（7=旧／32=新）。
        int cols = lines[1].Split(',').Length;
        TimeCardCsvFormat fmt;
        if (cols == 32) fmt = TimeCardCsvFormat.新_Kyuyo;
        else if (cols == 7) fmt = TimeCardCsvFormat.旧_TmCalc;
        else return Fail($"想定外の列数（{cols}列）です。7列(旧)または32列(新)のCSVを指定してください。");

        bool 年月不一致 = false;
        var rows = new List<TimeCardImportRow>();
        for (int li = 1; li < lines.Length; li++)
        {
            var line = lines[li];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var v = line.Split(',');
            if (v.Length != cols) continue;
            string no = (v[TcCol.従業員No] ?? "").Trim();
            if (no.Length == 0) continue;
            if (v[TcCol.年].Trim() != meta.対象年 || v[TcCol.月].Trim() != meta.対象月) 年月不一致 = true;

            byNo.TryGetValue(no, out var emp);
            var row = new TimeCardImportRow { No = no, 氏名 = (v[TcCol.氏名] ?? "").Trim(), Number = emp?.Number, 未登録 = emp is null };
            if (emp is not null)
            {
                double? D(int idx) => idx < v.Length && double.TryParse(v[idx], out var d) ? d : (double?)null;
                int? I(int idx) => idx < v.Length && int.TryParse(v[idx], out var n) ? n : (int?)null;

                var e = new PayrollEntry { Number = emp.Number.ToString(), 支給回 = 支給回 };
                bool isNew = fmt == TimeCardCsvFormat.新_Kyuyo;
                // マッピング定義テーブルを回して抽出（旧フォーマットは共通項目のみ）。
                foreach (var f in TcFields)
                {
                    if (f.新のみ && !isNew) continue;
                    f.設定(e, f.抽出(D, I));
                }
                if (isNew)
                {
                    // 検算：CSV残業金額合計(16-23) と 再計算時間外手当。
                    // あわせて、採用する時間外手当の内訳（略称＋金額・非ゼロのみ）を備考用に組み立てる。
                    int csvKin = 0;
                    var 内訳 = new List<string>();
                    foreach (var (列, 略称) in Tc残業金額内訳)
                    {
                        int kin = I(列) ?? 0;
                        csvKin += kin;
                        if (kin != 0) 内訳.Add($"{略称}{kin:N0}");
                    }
                    row.Csv残業金額合計 = csvKin;
                    row.残業内訳備考 = 内訳.Count > 0 ? "【時間外内訳】" + string.Join(" ", 内訳) : "";
                    try { row.再計算時間外手当 = _db.ComputeOvertime(e, emp.Number); } catch { /* 単価未設定等は検算スキップ */ }
                }
                row.転記 = e;
            }
            rows.Add(row);
        }
        return new TimeCardImportPlan(true, "", filePath, Path.GetFileName(filePath), fmt, 支給回, 対象年月, 年月不一致, rows);
    }

    /// <summary>取込計画を給与トランへ適用し、ログを記録してCSVを削除する。空欄は既存値を維持（旧仕様踏襲）。</summary>
    public TimeCardImportResult ApplyTimeCardImport(TimeCardImportPlan plan)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "Windows専用です。", 0, 0);
        if (!plan.Ok) return new(false, plan.Message, 0, 0);

        int applied = 0, skipped = 0, failed = 0;
        string? firstError = null;
        foreach (var r in plan.行)
        {
            if (r.未登録 || r.Number is null || r.転記 is null) { skipped++; continue; }
            int number = r.Number.Value;
            // 1人の計算失敗（例：乙欄なのに乙税表が無い等）でバッチ全体を止めないよう、従業員単位で握る。
            try
            {
                var e = _db.LoadPayrollEntry(number.ToString(), plan.支給回);
                var s = r.転記;
                bool isNew = plan.フォーマット == TimeCardCsvFormat.新_Kyuyo;

                // マッピング定義テーブルを回して転記（空欄＝nullは既存値を維持）。旧フォーマットは共通項目のみ。
                foreach (var f in TcFields)
                {
                    if (f.新のみ && !isNew) continue;
                    var val = f.取得(s);
                    if (val is not null) f.設定(e, val);
                }
                if (isNew)
                {
                    // 時間外手当はタイムカード計算データ側の残業金額（CSV列16〜23の合計）をそのまま採用する。
                    // （給与側の単価×時間による再計算は行わない。CalculatePayroll は入力済みの時間外手当をそのまま総額・税へ反映する。）
                    if (r.Csv残業金額合計 is not null) e.時間外手当 = r.Csv残業金額合計;
                    // 採用した時間外手当の内訳を給料明細の備考へ反映する（ユーザー指定：常に上書き）。
                    e.備考 = r.残業内訳備考;
                }
                var calc = _db.CalculatePayroll(e, number);
                _db.SavePayrollEntry(calc);
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                AppErrors.Log(ex, $"タイムカード取込エラー No.{r.No}");
                firstError ??= $"No.{r.No} {r.氏名}：取り込み中にエラーが発生しました。";
            }
        }

        // 取込ログを記録（取込済の証跡）。
        try
        {
            using var store = new SecureDataStore(_db.DataDir, DataProtectionScope.CurrentUser);
            EnsureTimeCardLogSchema(store.Connection);
            using var cmd = store.Connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO ""M_タイムカード取込ログ""
                (取込日時,対象年月,ファイル名,フォーマット,反映件数,スキップ件数)
                VALUES ($dt,$ym,$f,$fmt,$a,$s)";
            cmd.Parameters.AddWithValue("$dt", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$ym", plan.対象年月);
            cmd.Parameters.AddWithValue("$f", plan.ファイル名);
            cmd.Parameters.AddWithValue("$fmt", plan.フォーマット == TimeCardCsvFormat.新_Kyuyo ? "新(kyuyo)" : "旧(TmCalc)");
            cmd.Parameters.AddWithValue("$a", applied);
            cmd.Parameters.AddWithValue("$s", skipped);
            cmd.ExecuteNonQuery();
        }
        catch { /* ログ失敗は取込自体を止めない */ }

        // 失敗が無いときのみCSVを削除（二重取込防止）。失敗があればデータ修正後の再取込に備えて残す。
        if (failed == 0)
            try { File.Delete(plan.ファイルパス); } catch { }

        if (failed > 0)
        {
            var msg = $"一部の従業員で計算に失敗しました（反映 {applied} 名／失敗 {failed} 件／スキップ {skipped} 件）。" +
                      $"失敗した従業員は取り込まれていません。再取込できるようCSVは残しています。" +
                      (firstError is null ? "" : $" 最初のエラー：{firstError}");
            return new(false, msg, applied, skipped, failed);
        }
        return new(true, $"取込が完了しました（反映 {applied} 名／スキップ {skipped} 件）。", applied, skipped, 0);
    }

    /// <summary>タイムカード取込ログを新しい順に取得する。</summary>
    public IReadOnlyList<TimeCardImportLog> ListTimeCardImportLogs(int limit = 50)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<TimeCardImportLog>();
        using var store = new SecureDataStore(_db.DataDir, DataProtectionScope.CurrentUser);
        EnsureTimeCardLogSchema(store.Connection);
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = @"SELECT 取込日時,対象年月,ファイル名,フォーマット,反映件数,スキップ件数
            FROM ""M_タイムカード取込ログ"" ORDER BY Id DESC LIMIT $lim";
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<TimeCardImportLog>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new TimeCardImportLog(
                r.IsDBNull(0) ? "" : r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? 0 : r.GetInt32(4), r.IsDBNull(5) ? 0 : r.GetInt32(5)));
        return list;
    }
}

/// <summary>タイムカード転記ファイルの格納パス設定。</summary>
public sealed record TimeCardSetting(string 格納パス);

/// <summary>タイムカードCSVのフォーマット種別。旧＝7列(_TmCalc)／新＝32列(kyuyo)。</summary>
public enum TimeCardCsvFormat { 旧_TmCalc, 新_Kyuyo }

/// <summary>タイムカード取込プレビューの1行（1従業員）。転記予定値と検算情報を持つ。</summary>
public sealed class TimeCardImportRow
{
    public string No { get; set; } = "";
    public string 氏名 { get; set; } = "";
    public int? Number { get; set; }
    public bool 未登録 { get; set; }
    /// <summary>転記予定値（Number/支給回セット、対象項目のみ non-null）。未登録行は null。</summary>
    public PayrollEntry? 転記 { get; set; }
    /// <summary>CSV上の残業金額合計（新フォーマットのみ・検算用）。</summary>
    public int? Csv残業金額合計 { get; set; }
    /// <summary>採用した時間外手当の内訳（略称＋金額・非ゼロのみ）。備考へ書き出す。新フォーマットのみ。</summary>
    public string 残業内訳備考 { get; set; } = "";
    /// <summary>従業員マスタの単価で再計算した時間外手当（検算用）。</summary>
    public int? 再計算時間外手当 { get; set; }
    /// <summary>検算差額（CSV金額 − 再計算額）。0以外はハイライト対象。</summary>
    public int 検算差額 => (Csv残業金額合計 ?? 0) - (再計算時間外手当 ?? 0);
}

/// <summary>タイムカード取込のプレビュー計画（DB未反映）。UIで確認後に ApplyTimeCardImport へ渡す。</summary>
public sealed record TimeCardImportPlan(
    bool Ok, string Message, string ファイルパス, string ファイル名,
    TimeCardCsvFormat フォーマット, int 支給回, string 対象年月, bool 年月不一致,
    IReadOnlyList<TimeCardImportRow> 行);

/// <summary>タイムカード取込の実行結果。失敗件数＝計算エラー等で取り込めなかった従業員数（未登録スキップとは別）。</summary>
public sealed record TimeCardImportResult(bool Ok, string Message, int 反映件数, int スキップ件数, int 失敗件数 = 0);

/// <summary>タイムカード取込ログ1件。</summary>
public sealed record TimeCardImportLog(
    string 取込日時, string 対象年月, string ファイル名, string フォーマット, int 反映件数, int スキップ件数);
