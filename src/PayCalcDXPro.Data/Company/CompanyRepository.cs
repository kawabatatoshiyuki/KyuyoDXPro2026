using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.Company;

/// <summary>
/// 会社情報（M_会社情報）と、コンボ用コードマスタ（M_コード）の読み書き。
/// 現行 F_会社情報入力 のロジックを、パラメータ化SQLで安全に移植したもの。
/// 新規（空）DBでも動くよう、スキーマとコードマスタの初期値を必要に応じて用意する。
/// </summary>
public sealed class CompanyRepository
{
    private readonly SqliteConnection _conn;

    public CompanyRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>会社情報・コードマスタのスキーマを用意し、コードマスタが空なら初期値を投入する。</summary>
    public void EnsureSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS "M_会社情報" (
                "会社番号" INTEGER,
                "会社名カナ" TEXT, "会社名" TEXT, "事業所名" TEXT,
                "郵便番号" TEXT, "所在地カナ" TEXT, "所在地" TEXT,
                "代表者氏名カナ" TEXT, "代表者名" TEXT, "ＴＥＬ" TEXT,
                "連絡者名カナ" TEXT, "連絡者名" TEXT, "事業種目" TEXT,
                "番号区分" TEXT, "事業所マイナンバー" TEXT,
                "税務署名" TEXT, "署番号" TEXT, "整理番号" TEXT,
                "支払方法" TEXT, "支払金融機関" TEXT, "金融機関所在地" TEXT,
                "労働保険適用事業所" TEXT, "年末調整方法" TEXT, "残業手当計算端数処理" TEXT,
                "支給日登録フラグ" TEXT,
                PRIMARY KEY("会社番号")
            );
            """);
        Exec("""CREATE TABLE IF NOT EXISTS "M_コード" ("項目区分" TEXT, "コード" TEXT, "値" TEXT);""");

        // 会社情報に付随する4マスタ（現行スキーマに合わせて全列TEXT・番号列を主キー）。
        Exec("""CREATE TABLE IF NOT EXISTS "M_部門情報" ("部門番号" TEXT, "部門名" TEXT, PRIMARY KEY("部門番号"));""");
        Exec("""CREATE TABLE IF NOT EXISTS "M_役職情報" ("役職番号" TEXT, "役職名" TEXT, PRIMARY KEY("役職番号"));""");
        Exec("""CREATE TABLE IF NOT EXISTS "M_銀行情報" ("銀行番号" TEXT, "銀行名" TEXT, "支店名" TEXT, PRIMARY KEY("銀行番号"));""");
        Exec("""CREATE TABLE IF NOT EXISTS "M_報告書提出先情報" ("提出先番号" TEXT, "市区町村名" TEXT, "徴収義務者番号" TEXT, PRIMARY KEY("提出先番号"));""");

        // 移行済みDB（郵便番号列を持たない旧スキーマ）にも列を後付けする。
        EnsureColumn("M_会社情報", "郵便番号", "TEXT");

        SeedCodesIfMissing("020", ("1", "(A)一般事業所"), ("2", "(B)農林・水産・建設・清酒製造業"));
        SeedCodesIfMissing("021", ("1", "給与/賞与とは別に単独で調整"), ("2", "12月の給与で調整"), ("3", "最後の賞与で調整"));
        SeedCodesIfMissing("022", ("1", "四捨五入"), ("2", "切捨て"), ("3", "切上げ"));

        // 住宅借入金等特別控除の適用区分（現行 M_コード 項目区分 003=1回目／004=2回目。両者同一の29区分）。
        SeedCodesIfMissing("003", 住宅適用区分);
        SeedCodesIfMissing("004", 住宅適用区分);

        // 配偶者・扶養親族の収入種類（現行 M_コード 項目区分 002）。コード1=なし。
        SeedCodesIfMissing("002", ("1", ""), ("2", "給与"), ("3", "年金"), ("4", "所得"));
    }

    /// <summary>住宅借入金等特別控除の適用区分（現行どおり。コード1=なし）。003=1回目・004=2回目で共通。</summary>
    private static readonly (string コード, string 値)[] 住宅適用区分 =
    {
        ("1", ""), ("2", "住"), ("3", "住(持家)"), ("4", "増"), ("5", "認"), ("6", "認(持家)"), ("7", "震"), ("8", "震(持家)"),
        ("9", "住(特)"), ("10", "住(持家)(特)"), ("11", "増(特)"), ("12", "認(特)"), ("13", "認(持家)(特)"), ("14", "震(特)"), ("15", "震(持家)(特)"),
        ("16", "住(特特)"), ("17", "住(持家)(特特)"), ("18", "増(特特)"), ("19", "認(特特)"), ("20", "認(持家)(特特)"), ("21", "震(特特)"), ("22", "震(持家)(特特)"),
        ("23", "住(特特特)"), ("24", "住(持家)(特特特)"), ("25", "増(特特特)"), ("26", "認(特特特)"), ("27", "認(持家)(特特特)"), ("28", "震(特特特)"), ("29", "震(持家)(特特特)"),
    };

    /// <summary>コンボ用にコードマスタから選択肢を取得する（コードの数値順）。</summary>
    public IReadOnlyList<CodeOption> GetCodeOptions(string category)
    {
        var list = new List<CodeOption>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT コード, 値 FROM M_コード WHERE 項目区分 = $c ORDER BY CAST(コード AS INTEGER)";
        cmd.Parameters.AddWithValue("$c", category);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CodeOption(r.IsDBNull(0) ? "" : r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)));
        return list;
    }

    /// <summary>会社情報を1件読み込む。未登録なら null。</summary>
    public CompanyInfo? Load()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT 会社名カナ, 会社名, 事業所名, 所在地カナ, 所在地, 代表者氏名カナ, 代表者名, ＴＥＬ,
                   連絡者名カナ, 連絡者名, 事業種目, 番号区分, 事業所マイナンバー, 税務署名, 署番号, 整理番号,
                   支払方法, 支払金融機関, 金融機関所在地, 労働保険適用事業所, 年末調整方法, 残業手当計算端数処理,
                   支給日登録フラグ, 郵便番号
            FROM M_会社情報 LIMIT 1
            """;
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        return new CompanyInfo
        {
            会社名カナ = S(0), 会社名 = S(1), 事業所名 = S(2),
            所在地カナ = S(3), 所在地 = S(4),
            代表者氏名カナ = S(5), 代表者名 = S(6), ＴＥＬ = S(7),
            連絡者名カナ = S(8), 連絡者名 = S(9), 事業種目 = S(10),
            番号区分 = string.IsNullOrEmpty(S(11)) ? "1" : S(11),
            事業所マイナンバー = S(12),
            税務署名 = S(13), 署番号 = S(14), 整理番号 = S(15),
            支払方法 = S(16), 支払金融機関 = S(17), 金融機関所在地 = S(18),
            労働保険適用事業所 = string.IsNullOrEmpty(S(19)) ? "1" : S(19),
            年末調整方法 = string.IsNullOrEmpty(S(20)) ? "1" : S(20),
            残業手当計算端数処理 = string.IsNullOrEmpty(S(21)) ? "1" : S(21),
            支給日登録フラグ = string.IsNullOrEmpty(S(22)) ? "0" : S(22),
            郵便番号 = S(23),
        };
    }

    /// <summary>会社情報を保存（無ければ追加、あれば更新）。1社1レコード運用。</summary>
    public void Save(CompanyInfo c)
    {
        // 個人事業主のマイナンバーは平文で M_会社情報 に保存しない（セキュア入力は次段）。
        string マイナンバー値 = c.番号区分 == "1" ? c.事業所マイナンバー : "";

        bool exists;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM M_会社情報";
            exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        using var up = _conn.CreateCommand();
        if (exists)
        {
            up.CommandText = """
                UPDATE M_会社情報 SET
                    会社名カナ=$会社名カナ, 会社名=$会社名, 事業所名=$事業所名,
                    郵便番号=$郵便番号, 所在地カナ=$所在地カナ, 所在地=$所在地,
                    代表者氏名カナ=$代表者氏名カナ, 代表者名=$代表者名, ＴＥＬ=$ＴＥＬ,
                    連絡者名カナ=$連絡者名カナ, 連絡者名=$連絡者名, 事業種目=$事業種目,
                    番号区分=$番号区分, 事業所マイナンバー=$マイナンバー,
                    税務署名=$税務署名, 署番号=$署番号, 整理番号=$整理番号,
                    支払方法=$支払方法, 支払金融機関=$支払金融機関, 金融機関所在地=$金融機関所在地,
                    労働保険適用事業所=$労働保険, 年末調整方法=$年末調整, 残業手当計算端数処理=$端数処理
                """;
        }
        else
        {
            up.CommandText = """
                INSERT INTO M_会社情報 (
                    会社番号, 会社名カナ, 会社名, 事業所名, 郵便番号, 所在地カナ, 所在地,
                    代表者氏名カナ, 代表者名, ＴＥＬ, 連絡者名カナ, 連絡者名, 事業種目,
                    番号区分, 事業所マイナンバー, 税務署名, 署番号, 整理番号,
                    支払方法, 支払金融機関, 金融機関所在地,
                    労働保険適用事業所, 年末調整方法, 残業手当計算端数処理, 支給日登録フラグ
                ) VALUES (
                    1, $会社名カナ, $会社名, $事業所名, $郵便番号, $所在地カナ, $所在地,
                    $代表者氏名カナ, $代表者名, $ＴＥＬ, $連絡者名カナ, $連絡者名, $事業種目,
                    $番号区分, $マイナンバー, $税務署名, $署番号, $整理番号,
                    $支払方法, $支払金融機関, $金融機関所在地,
                    $労働保険, $年末調整, $端数処理, $支給日登録フラグ
                )
                """;
            up.Parameters.AddWithValue("$支給日登録フラグ", c.支給日登録フラグ ?? "0");
        }

        up.Parameters.AddWithValue("$会社名カナ", c.会社名カナ ?? "");
        up.Parameters.AddWithValue("$会社名", c.会社名 ?? "");
        up.Parameters.AddWithValue("$事業所名", c.事業所名 ?? "");
        up.Parameters.AddWithValue("$郵便番号", c.郵便番号 ?? "");
        up.Parameters.AddWithValue("$所在地カナ", c.所在地カナ ?? "");
        up.Parameters.AddWithValue("$所在地", c.所在地 ?? "");
        up.Parameters.AddWithValue("$代表者氏名カナ", c.代表者氏名カナ ?? "");
        up.Parameters.AddWithValue("$代表者名", c.代表者名 ?? "");
        up.Parameters.AddWithValue("$ＴＥＬ", c.ＴＥＬ ?? "");
        up.Parameters.AddWithValue("$連絡者名カナ", c.連絡者名カナ ?? "");
        up.Parameters.AddWithValue("$連絡者名", c.連絡者名 ?? "");
        up.Parameters.AddWithValue("$事業種目", c.事業種目 ?? "");
        up.Parameters.AddWithValue("$番号区分", c.番号区分 ?? "1");
        up.Parameters.AddWithValue("$マイナンバー", マイナンバー値 ?? "");
        up.Parameters.AddWithValue("$税務署名", c.税務署名 ?? "");
        up.Parameters.AddWithValue("$署番号", c.署番号 ?? "");
        up.Parameters.AddWithValue("$整理番号", c.整理番号 ?? "");
        up.Parameters.AddWithValue("$支払方法", c.支払方法 ?? "");
        up.Parameters.AddWithValue("$支払金融機関", c.支払金融機関 ?? "");
        up.Parameters.AddWithValue("$金融機関所在地", c.金融機関所在地 ?? "");
        up.Parameters.AddWithValue("$労働保険", c.労働保険適用事業所 ?? "1");
        up.Parameters.AddWithValue("$年末調整", c.年末調整方法 ?? "1");
        up.Parameters.AddWithValue("$端数処理", c.残業手当計算端数処理 ?? "1");
        up.ExecuteNonQuery();
    }

    /// <summary>部門の選択肢（先頭固定行=0「未設定」も含む。番号順）。従業員等のコンボ用。</summary>
    public IReadOnlyList<CodeOption> GetDepartmentOptions()
        => QueryOptions("SELECT 部門番号, 部門名 FROM M_部門情報 ORDER BY CAST(部門番号 AS INTEGER)");

    /// <summary>役職の選択肢（先頭固定行=0「未設定」も含む。番号順）。</summary>
    public IReadOnlyList<CodeOption> GetPositionOptions()
        => QueryOptions("SELECT 役職番号, 役職名 FROM M_役職情報 ORDER BY CAST(役職番号 AS INTEGER)");

    /// <summary>振込銀行の選択肢（先頭固定行=1「現金支給」も含む。支店名があれば併記。番号順）。</summary>
    public IReadOnlyList<CodeOption> GetBankOptions()
        => QueryOptions("SELECT 銀行番号, 銀行名 || IFNULL(' ' || 支店名, '') FROM M_銀行情報 ORDER BY CAST(銀行番号 AS INTEGER)");

    /// <summary>報告書提出先（住民税納付先 市区町村）の選択肢（先頭固定行=1「特別徴収対象外」も含む。番号順）。</summary>
    public IReadOnlyList<CodeOption> GetRecipientOptions()
        => QueryOptions("SELECT 提出先番号, 市区町村名 FROM M_報告書提出先情報 ORDER BY CAST(提出先番号 AS INTEGER)");

    /// <summary>銀行番号→(銀行名,支店名) の対応表（銀行振込一覧表など、銀行名/支店名を別々に使う帳票用）。</summary>
    public Dictionary<string, (string 銀行名, string 支店名)> GetBankMap()
    {
        var d = new Dictionary<string, (string, string)>();
        foreach (var row in Query("SELECT 銀行番号, 銀行名, IFNULL(支店名,'') FROM M_銀行情報", 3))
            d[row[0]] = (row[1], row[2]);
        return d;
    }

    private IReadOnlyList<CodeOption> QueryOptions(string sql)
    {
        var list = new List<CodeOption>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CodeOption(r.IsDBNull(0) ? "" : r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)));
        return list;
    }

    /// <summary>
    /// 部門/役職/銀行/報告書提出先の4マスタを読み込む。
    /// いずれも先頭の固定レコード（部門/役職=番号0、銀行/提出先=番号1）はユーザー編集対象外なので除外する。
    /// </summary>
    public CompanyMasters LoadMasters()
    {
        var m = new CompanyMasters();

        foreach (var name in Query("SELECT 部門名 FROM M_部門情報 WHERE 部門番号 <> '0' ORDER BY CAST(部門番号 AS INTEGER)", 1))
            m.部門.Add(new DepartmentRow { 部門名 = name[0] });

        foreach (var name in Query("SELECT 役職名 FROM M_役職情報 WHERE 役職番号 <> '0' ORDER BY CAST(役職番号 AS INTEGER)", 1))
            m.役職.Add(new PositionRow { 役職名 = name[0] });

        foreach (var row in Query("SELECT 銀行名, 支店名 FROM M_銀行情報 WHERE 銀行番号 <> '1' ORDER BY CAST(銀行番号 AS INTEGER)", 2))
            m.銀行.Add(new BankRow { 銀行名 = row[0], 支店名 = row[1] });

        foreach (var row in Query("SELECT 市区町村名, 徴収義務者番号 FROM M_報告書提出先情報 WHERE 提出先番号 <> '1' ORDER BY CAST(提出先番号 AS INTEGER)", 2))
            m.提出先.Add(new RecipientRow { 市区町村名 = row[0], 徴収義務者番号 = row[1] });

        return m;
    }

    /// <summary>
    /// 4マスタを丸ごと保存する（現行同様、全削除→固定レコード＋ユーザー行を連番で再投入）。
    /// 4テーブルを1トランザクションで原子的に更新する。主キー列が空の行は除外する。
    /// </summary>
    public void SaveMasters(CompanyMasters m)
    {
        using var tx = _conn.BeginTransaction();

        // 部門（先頭=番号0「未設定」、ユーザー行は1から採番）
        ExecTx(tx, "DELETE FROM M_部門情報");
        InsertMaster(tx, "M_部門情報", "部門番号", new[] { "部門名" }, "0", new[] { "未設定" });
        int n = 1;
        foreach (var d in m.部門.Where(x => !string.IsNullOrWhiteSpace(x.部門名)))
            InsertMaster(tx, "M_部門情報", "部門番号", new[] { "部門名" }, n++.ToString(), new[] { d.部門名.Trim() });

        // 役職（先頭=番号0「未設定」、ユーザー行は1から採番）
        ExecTx(tx, "DELETE FROM M_役職情報");
        InsertMaster(tx, "M_役職情報", "役職番号", new[] { "役職名" }, "0", new[] { "未設定" });
        n = 1;
        foreach (var p in m.役職.Where(x => !string.IsNullOrWhiteSpace(x.役職名)))
            InsertMaster(tx, "M_役職情報", "役職番号", new[] { "役職名" }, n++.ToString(), new[] { p.役職名.Trim() });

        // 銀行（先頭=番号1「現金支給」、ユーザー行は2から採番）
        ExecTx(tx, "DELETE FROM M_銀行情報");
        InsertMaster(tx, "M_銀行情報", "銀行番号", new[] { "銀行名", "支店名" }, "1", new[] { "現金支給", null });
        n = 2;
        foreach (var b in m.銀行.Where(x => !string.IsNullOrWhiteSpace(x.銀行名)))
            InsertMaster(tx, "M_銀行情報", "銀行番号", new[] { "銀行名", "支店名" }, n++.ToString(),
                new[] { b.銀行名.Trim(), NullIfBlank(b.支店名) });

        // 報告書提出先（先頭=番号1「特別徴収対象外」、ユーザー行は2から採番）
        ExecTx(tx, "DELETE FROM M_報告書提出先情報");
        InsertMaster(tx, "M_報告書提出先情報", "提出先番号", new[] { "市区町村名", "徴収義務者番号" }, "1",
            new[] { "特別徴収対象外", null });
        n = 2;
        foreach (var r in m.提出先.Where(x => !string.IsNullOrWhiteSpace(x.市区町村名)))
            InsertMaster(tx, "M_報告書提出先情報", "提出先番号", new[] { "市区町村名", "徴収義務者番号" }, n++.ToString(),
                new[] { r.市区町村名.Trim(), NullIfBlank(r.徴収義務者番号) });

        tx.Commit();
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();

    /// <summary>1テーブルへ「番号列＋データ列」を1行INSERT。値がnullならNULLを格納。</summary>
    private void InsertMaster(SqliteTransaction tx, string table, string keyCol, string[] dataCols,
        string keyVal, string?[] dataVals)
    {
        var allCols = new[] { keyCol }.Concat(dataCols).ToArray();
        var paramNames = allCols.Select((_, i) => "$p" + i).ToArray();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            $"INSERT INTO {table} ({string.Join(", ", allCols)}) VALUES ({string.Join(", ", paramNames)})";
        cmd.Parameters.AddWithValue("$p0", keyVal);
        for (int i = 0; i < dataVals.Length; i++)
            cmd.Parameters.AddWithValue("$p" + (i + 1), (object?)dataVals[i] ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>colCount列を文字列で読み出すヘルパ。NULLは空文字に正規化する。</summary>
    private List<string[]> Query(string sql, int colCount)
    {
        var rows = new List<string[]>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new string[colCount];
            for (int i = 0; i < colCount; i++)
                row[i] = r.IsDBNull(i) ? "" : r.GetString(i);
            rows.Add(row);
        }
        return rows;
    }

    private void ExecTx(SqliteTransaction tx, string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void SeedCodesIfMissing(string category, params (string コード, string 値)[] items)
    {
        using (var check = _conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM M_コード WHERE 項目区分 = $c";
            check.Parameters.AddWithValue("$c", category);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return;
        }
        foreach (var (コード, 値) in items)
        {
            using var ins = _conn.CreateCommand();
            ins.CommandText = "INSERT INTO M_コード (項目区分, コード, 値) VALUES ($c, $k, $v)";
            ins.Parameters.AddWithValue("$c", category);
            ins.Parameters.AddWithValue("$k", コード);
            ins.Parameters.AddWithValue("$v", 値);
            ins.ExecuteNonQuery();
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>テーブルに指定列が無ければ ALTER TABLE で追加する（既存DBへの列追加用）。</summary>
    private void EnsureColumn(string table, string column, string type)
    {
        bool exists = false;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                // 1列目(name)が列名
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }
        if (!exists)
            Exec($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type}");
    }
}
