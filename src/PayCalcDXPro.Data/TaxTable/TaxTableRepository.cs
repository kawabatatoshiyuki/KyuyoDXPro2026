using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.TaxTable;

/// <summary>
/// 所得税（月額表）系テーブルの読み書き。
/// T_月額表別表1〜4／T_賞与源泉徴収税額甲・乙。各テーブルは No 順の全行を入れ替えて保存する。
/// </summary>
public sealed class TaxTableRepository
{
    private readonly SqliteConnection _conn;

    public TaxTableRepository(SqliteConnection conn) => _conn = conn;

    public void EnsureSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_月額表別表1" (
                "No" INTEGER, "給与額_自" INTEGER, "給与額_至" INTEGER, "控除率" REAL, "控除額" INTEGER,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_月額表別表2" (
                "No" INTEGER, "配偶者控除" INTEGER, "扶養控除" INTEGER,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_月額表別表3" (
                "No" INTEGER, "給与額_自" INTEGER, "給与額_至" INTEGER, "控除額" INTEGER,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_月額表別表4" (
                "No" INTEGER, "給与額_自" INTEGER, "給与額_至" INTEGER, "税率" REAL, "控除額" INTEGER,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_賞与源泉徴収税額甲" (
                "No" INTEGER,
                "金額0人" INTEGER, "金額1人" INTEGER, "金額2人" INTEGER, "金額3人" INTEGER,
                "金額4人" INTEGER, "金額5人" INTEGER, "金額6人" INTEGER, "金額7人以上" INTEGER,
                "賞与に乗ずる率" REAL,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_賞与源泉徴収税額乙" (
                "No" INTEGER, "金額" INTEGER, "賞与に乗ずる率" REAL,
                PRIMARY KEY("No")
            );
            """);
        // 乙欄（従たる給与）源泉税の各表。列名・型は現行DB（T_乙税第１〜３表・T_乙税階差）と同一に保つ
        // （LegacyMasterRepository.LoadOtsuTaxTables が同じ列名で読むため）。
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_乙税第１表" (
                "No" INTEGER, "給与額_自" INTEGER, "給与額_至" INTEGER, "控除率" INTEGER, "控除額" INTEGER,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_乙税第２表" (
                "No" INTEGER, "控除額" INTEGER,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_乙税第３表" (
                "No" INTEGER, "給与額_自" INTEGER, "給与額_至" INTEGER, "控除率" INTEGER, "控除額" INTEGER,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_乙税階差" (
                "No" INTEGER, "給与額_自" INTEGER, "給与額_至" INTEGER, "階差" INTEGER,
                PRIMARY KEY("No")
            );
            """);

        // 空のテーブルにのみ現行DB由来の初期データを投入。
        if (IsEmpty("T_月額表別表1")) Save別表1(TaxTableSeed.別表1());
        if (IsEmpty("T_月額表別表2")) Save別表2(TaxTableSeed.別表2());
        if (IsEmpty("T_月額表別表3")) Save別表3(TaxTableSeed.別表3());
        if (IsEmpty("T_月額表別表4")) Save別表4(TaxTableSeed.別表4());
        if (IsEmpty("T_賞与源泉徴収税額甲")) Save賞与甲(TaxTableSeed.賞与甲());
        if (IsEmpty("T_賞与源泉徴収税額乙")) Save賞与乙(TaxTableSeed.賞与乙());
        if (IsEmpty("T_乙税第１表")) Save乙税第1(TaxTableSeed.乙税第1());
        if (IsEmpty("T_乙税第２表")) Save乙税第2(TaxTableSeed.乙税第2());
        if (IsEmpty("T_乙税第３表")) Save乙税第3(TaxTableSeed.乙税第3());
        if (IsEmpty("T_乙税階差")) Save乙税階差(TaxTableSeed.乙税階差());
    }

    public TaxTableBundle Load() => new()
    {
        別表1 = Load別表1(),
        別表2 = Load別表2(),
        別表3 = Load別表3(),
        別表4 = Load別表4(),
        賞与甲 = Load賞与甲(),
        賞与乙 = Load賞与乙(),
        乙税第1 = Load乙税第1(),
        乙税第2 = Load乙税第2(),
        乙税第3 = Load乙税第3(),
        乙税階差 = Load乙税階差(),
    };

    /// <summary>全テーブルを1トランザクションで全置換保存する。</summary>
    public void Save(TaxTableBundle b)
    {
        using var tx = _conn.BeginTransaction();
        Save別表1(b.別表1, tx);
        Save別表2(b.別表2, tx);
        Save別表3(b.別表3, tx);
        Save別表4(b.別表4, tx);
        Save賞与甲(b.賞与甲, tx);
        Save賞与乙(b.賞与乙, tx);
        Save乙税第1(b.乙税第1, tx);
        Save乙税第2(b.乙税第2, tx);
        Save乙税第3(b.乙税第3, tx);
        Save乙税階差(b.乙税階差, tx);
        tx.Commit();
    }

    // ---- 別表1 ----
    private List<Table1Row> Load別表1()
    {
        var list = new List<Table1Row>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 給与額_自, 給与額_至, 控除率, 控除額 FROM T_月額表別表1 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Table1Row { No = I(r, 0)!.Value, 給与額自 = I(r, 1), 給与額至 = I(r, 2), 控除率 = D(r, 3), 控除額 = I(r, 4) });
        return list;
    }

    private void Save別表1(IReadOnlyList<Table1Row> rows, SqliteTransaction? tx = null)
    {
        Replace("T_月額表別表1", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_月額表別表1 (No, 給与額_自, 給与額_至, 控除率, 控除額) VALUES ($no,$a,$b,$c,$d)",
                    tx, ("$no", row.No), ("$a", row.給与額自), ("$b", row.給与額至), ("$c", row.控除率), ("$d", row.控除額));
        });
    }

    // ---- 別表2 ----
    private List<Table2Row> Load別表2()
    {
        var list = new List<Table2Row>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 配偶者控除, 扶養控除 FROM T_月額表別表2 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Table2Row { No = I(r, 0)!.Value, 配偶者控除 = I(r, 1), 扶養控除 = I(r, 2) });
        return list;
    }

    private void Save別表2(IReadOnlyList<Table2Row> rows, SqliteTransaction? tx = null)
    {
        Replace("T_月額表別表2", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_月額表別表2 (No, 配偶者控除, 扶養控除) VALUES ($no,$a,$b)",
                    tx, ("$no", row.No), ("$a", row.配偶者控除), ("$b", row.扶養控除));
        });
    }

    // ---- 別表3 ----
    private List<Table3Row> Load別表3()
    {
        var list = new List<Table3Row>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 給与額_自, 給与額_至, 控除額 FROM T_月額表別表3 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Table3Row { No = I(r, 0)!.Value, 給与額自 = I(r, 1), 給与額至 = I(r, 2), 控除額 = I(r, 3) });
        return list;
    }

    private void Save別表3(IReadOnlyList<Table3Row> rows, SqliteTransaction? tx = null)
    {
        Replace("T_月額表別表3", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_月額表別表3 (No, 給与額_自, 給与額_至, 控除額) VALUES ($no,$a,$b,$c)",
                    tx, ("$no", row.No), ("$a", row.給与額自), ("$b", row.給与額至), ("$c", row.控除額));
        });
    }

    // ---- 別表4 ----
    private List<Table4Row> Load別表4()
    {
        var list = new List<Table4Row>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 給与額_自, 給与額_至, 税率, 控除額 FROM T_月額表別表4 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Table4Row { No = I(r, 0)!.Value, 給与額自 = I(r, 1), 給与額至 = I(r, 2), 税率 = D(r, 3), 控除額 = I(r, 4) });
        return list;
    }

    private void Save別表4(IReadOnlyList<Table4Row> rows, SqliteTransaction? tx = null)
    {
        Replace("T_月額表別表4", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_月額表別表4 (No, 給与額_自, 給与額_至, 税率, 控除額) VALUES ($no,$a,$b,$c,$d)",
                    tx, ("$no", row.No), ("$a", row.給与額自), ("$b", row.給与額至), ("$c", row.税率), ("$d", row.控除額));
        });
    }

    // ---- 賞与甲 ----
    private List<BonusKouRow> Load賞与甲()
    {
        var list = new List<BonusKouRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT No, 金額0人, 金額1人, 金額2人, 金額3人, 金額4人, 金額5人, 金額6人, 金額7人以上, 賞与に乗ずる率
            FROM T_賞与源泉徴収税額甲 ORDER BY No
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BonusKouRow
            {
                No = I(r, 0)!.Value,
                金額0人 = I(r, 1), 金額1人 = I(r, 2), 金額2人 = I(r, 3), 金額3人 = I(r, 4),
                金額4人 = I(r, 5), 金額5人 = I(r, 6), 金額6人 = I(r, 7), 金額7人以上 = I(r, 8),
                率 = D(r, 9),
            });
        return list;
    }

    private void Save賞与甲(IReadOnlyList<BonusKouRow> rows, SqliteTransaction? tx = null)
    {
        Replace("T_賞与源泉徴収税額甲", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("""
                    INSERT INTO T_賞与源泉徴収税額甲
                        (No, 金額0人, 金額1人, 金額2人, 金額3人, 金額4人, 金額5人, 金額6人, 金額7人以上, 賞与に乗ずる率)
                    VALUES ($no,$k0,$k1,$k2,$k3,$k4,$k5,$k6,$k7,$r)
                    """,
                    tx, ("$no", row.No), ("$k0", row.金額0人), ("$k1", row.金額1人), ("$k2", row.金額2人),
                    ("$k3", row.金額3人), ("$k4", row.金額4人), ("$k5", row.金額5人), ("$k6", row.金額6人),
                    ("$k7", row.金額7人以上), ("$r", row.率));
        });
    }

    // ---- 賞与乙 ----
    private List<BonusOtsuRow> Load賞与乙()
    {
        var list = new List<BonusOtsuRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 金額, 賞与に乗ずる率 FROM T_賞与源泉徴収税額乙 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BonusOtsuRow { No = I(r, 0)!.Value, 金額 = I(r, 1), 率 = D(r, 2) });
        return list;
    }

    private void Save賞与乙(IReadOnlyList<BonusOtsuRow> rows, SqliteTransaction? tx = null)
    {
        Replace("T_賞与源泉徴収税額乙", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_賞与源泉徴収税額乙 (No, 金額, 賞与に乗ずる率) VALUES ($no,$a,$r)",
                    tx, ("$no", row.No), ("$a", row.金額), ("$r", row.率));
        });
    }

    // ---- 乙税第1表 ----
    private List<OtsuTax1Row> Load乙税第1()
    {
        var list = new List<OtsuTax1Row>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 給与額_自, 給与額_至, 控除率, 控除額 FROM T_乙税第１表 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OtsuTax1Row { No = I(r, 0)!.Value, 給与額自 = I(r, 1), 給与額至 = I(r, 2), 控除率 = D(r, 3), 控除額 = I(r, 4) });
        return list;
    }

    private void Save乙税第1(IReadOnlyList<OtsuTax1Row> rows, SqliteTransaction? tx = null)
    {
        Replace("T_乙税第１表", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_乙税第１表 (No, 給与額_自, 給与額_至, 控除率, 控除額) VALUES ($no,$a,$b,$c,$d)",
                    tx, ("$no", row.No), ("$a", row.給与額自), ("$b", row.給与額至), ("$c", row.控除率), ("$d", row.控除額));
        });
    }

    // ---- 乙税第2表（1レコード） ----
    private List<OtsuTax2Row> Load乙税第2()
    {
        var list = new List<OtsuTax2Row>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 控除額 FROM T_乙税第２表 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OtsuTax2Row { No = I(r, 0)!.Value, 控除額 = I(r, 1) });
        return list;
    }

    private void Save乙税第2(IReadOnlyList<OtsuTax2Row> rows, SqliteTransaction? tx = null)
    {
        Replace("T_乙税第２表", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_乙税第２表 (No, 控除額) VALUES ($no,$a)",
                    tx, ("$no", row.No), ("$a", row.控除額));
        });
    }

    // ---- 乙税第3表 ----
    private List<OtsuTax3Row> Load乙税第3()
    {
        var list = new List<OtsuTax3Row>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 給与額_自, 給与額_至, 控除率, 控除額 FROM T_乙税第３表 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OtsuTax3Row { No = I(r, 0)!.Value, 給与額自 = I(r, 1), 給与額至 = I(r, 2), 控除率 = D(r, 3), 控除額 = I(r, 4) });
        return list;
    }

    private void Save乙税第3(IReadOnlyList<OtsuTax3Row> rows, SqliteTransaction? tx = null)
    {
        Replace("T_乙税第３表", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_乙税第３表 (No, 給与額_自, 給与額_至, 控除率, 控除額) VALUES ($no,$a,$b,$c,$d)",
                    tx, ("$no", row.No), ("$a", row.給与額自), ("$b", row.給与額至), ("$c", row.控除率), ("$d", row.控除額));
        });
    }

    // ---- 乙税階差 ----
    private List<OtsuKaisaRow> Load乙税階差()
    {
        var list = new List<OtsuKaisaRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, 給与額_自, 給与額_至, 階差 FROM T_乙税階差 ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OtsuKaisaRow { No = I(r, 0)!.Value, 給与額自 = I(r, 1), 給与額至 = I(r, 2), 階差 = I(r, 3) });
        return list;
    }

    private void Save乙税階差(IReadOnlyList<OtsuKaisaRow> rows, SqliteTransaction? tx = null)
    {
        Replace("T_乙税階差", rows.Count == 0, tx, () =>
        {
            foreach (var row in rows)
                Insert("INSERT INTO T_乙税階差 (No, 給与額_自, 給与額_至, 階差) VALUES ($no,$a,$b,$c)",
                    tx, ("$no", row.No), ("$a", row.給与額自), ("$b", row.給与額至), ("$c", row.階差));
        });
    }

    // ---- 共通ヘルパ ----
    private bool IsEmpty(string table)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 0;
    }

    /// <summary>テーブルを全削除してから書き込み処理を実行する。空指定なら削除のみ。</summary>
    private void Replace(string table, bool emptyOnly, SqliteTransaction? tx, Action write)
    {
        using (var del = _conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM \"{table}\"";
            del.ExecuteNonQuery();
        }
        if (!emptyOnly) write();
    }

    private void Insert(string sql, SqliteTransaction? tx, params (string Name, object? Value)[] ps)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static int? I(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : (int)r.GetInt64(i);
    private static double? D(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
