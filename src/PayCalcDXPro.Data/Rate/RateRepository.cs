using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.Rate;

/// <summary>保険料率（T_料率、1レコード No=1）の読み書き。</summary>
public sealed class RateRepository
{
    private readonly SqliteConnection _conn;
    private const int No = 1;

    public RateRepository(SqliteConnection conn) => _conn = conn;

    public void EnsureSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_料率" (
                "No" INTEGER,
                "健康保険一般料率" REAL DEFAULT 0, "健康保険被保険者負担分" REAL,
                "厚生年金一般料率" REAL DEFAULT 0, "厚生年金被保険者負担分" REAL,
                "介護保険一般料率" REAL DEFAULT 0, "介護保険被保険者負担分" REAL,
                "子育支援一般料率" REAL DEFAULT 0, "子育支援被保険者負担分" REAL,
                "厚生年金基金一般料率" REAL DEFAULT 0, "厚生年金基金被保険者負担分" REAL,
                "雇用保険一般" REAL DEFAULT 0, "雇用保険一般以外" REAL,
                PRIMARY KEY("No")
            );
            """);
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_料率保険料額表" (
                "No" INTEGER, "報酬月額_自" INTEGER, "報酬月額_至" INTEGER,
                "等級_短期" INTEGER, "等級_長期" INTEGER,
                "標準報酬月額_短期" INTEGER, "標準報酬月額_長期" INTEGER,
                "健康保険" REAL, "厚生年金" REAL, "介護保険" REAL, "子育支援" REAL, "厚生年金基金" REAL,
                PRIMARY KEY("No")
            );
            """);

        // 料率：行が無ければ作成。全料率が0/空（未設定）なら現行DB由来の初期値を投入。
        using (var chk = _conn.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(*) FROM T_料率 WHERE No = $n";
            chk.Parameters.AddWithValue("$n", No);
            if (Convert.ToInt64(chk.ExecuteScalar()) == 0)
                Exec($"INSERT INTO T_料率 (No) VALUES ({No})");
        }
        using (var sum = _conn.CreateCommand())
        {
            // 主要な一般料率がすべて0/空なら未設定とみなしてシード。
            sum.CommandText = """
                SELECT IFNULL(健康保険一般料率,0)+IFNULL(厚生年金一般料率,0)+IFNULL(介護保険一般料率,0)
                       +IFNULL(厚生年金基金一般料率,0)+IFNULL(雇用保険一般,0)
                FROM T_料率 WHERE No = $n
                """;
            sum.Parameters.AddWithValue("$n", No);
            if (Convert.ToDouble(sum.ExecuteScalar() ?? 0.0) == 0.0)
                Save(RateSeed.料率());
        }

        // 子育支援料率の後方互換シード：他の料率は設定済みで子育支援だけ未設定（0/NULL）の既存DBにも投入する。
        // 上の一括シード条件（主要一般料率の合計）に子育支援が含まれないため、個別に補う。移行元 PayCalc2027.db T_料率：一般0.23／折半0.115。
        Exec($"UPDATE T_料率 SET 子育支援一般料率 = 0.23 WHERE No = {No} AND IFNULL(子育支援一般料率, 0) = 0");
        Exec($"UPDATE T_料率 SET 子育支援被保険者負担分 = 0.115 WHERE No = {No} AND IFNULL(子育支援被保険者負担分, 0) = 0");

        // 保険料額表：空なら現行DB由来の50等級を投入。
        using (var chk = _conn.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(*) FROM T_料率保険料額表";
            if (Convert.ToInt64(chk.ExecuteScalar()) == 0)
                SaveTable(RateSeed.額表());
        }

        // 子育支援額の後方互換：未設定(NULL)の等級を 標準報酬月額(短期)×折半0.115% で埋める（既存DB対応）。
        Exec("UPDATE T_料率保険料額表 SET 子育支援 = ROUND(標準報酬月額_短期 * 0.00115, 1) WHERE 子育支援 IS NULL");
    }

    public RateSettings Load()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT 健康保険一般料率, 健康保険被保険者負担分, 厚生年金一般料率, 厚生年金被保険者負担分,
                   介護保険一般料率, 介護保険被保険者負担分, 子育支援一般料率, 子育支援被保険者負担分,
                   厚生年金基金一般料率, 厚生年金基金被保険者負担分, 雇用保険一般, 雇用保険一般以外
            FROM T_料率 WHERE No = $n LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$n", No);
        using var r = cmd.ExecuteReader();
        var s = new RateSettings();
        if (!r.Read()) return s;

        double? D(int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
        s.健康保険一般 = D(0); s.健康保険本人 = D(1);
        s.厚生年金一般 = D(2); s.厚生年金本人 = D(3);
        s.介護保険一般 = D(4); s.介護保険本人 = D(5);
        s.子育支援一般 = D(6); s.子育支援本人 = D(7);
        s.厚生年金基金一般 = D(8); s.厚生年金基金本人 = D(9);
        s.雇用保険一般 = D(10); s.雇用保険一般以外 = D(11);
        return s;
    }

    public void Save(RateSettings s)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE T_料率 SET
                健康保険一般料率=$h, 健康保険被保険者負担分=$hp,
                厚生年金一般料率=$k, 厚生年金被保険者負担分=$kp,
                介護保険一般料率=$kg, 介護保険被保険者負担分=$kgp,
                子育支援一般料率=$ko, 子育支援被保険者負担分=$kop,
                厚生年金基金一般料率=$kk, 厚生年金基金被保険者負担分=$kkp,
                雇用保険一般=$ko1, 雇用保険一般以外=$ko2
            WHERE No=$n
            """;
        cmd.Parameters.AddWithValue("$n", No);
        void P(string name, double? v) => cmd.Parameters.AddWithValue(name, (object?)v ?? DBNull.Value);
        P("$h", s.健康保険一般); P("$hp", s.健康保険本人);
        P("$k", s.厚生年金一般); P("$kp", s.厚生年金本人);
        P("$kg", s.介護保険一般); P("$kgp", s.介護保険本人);
        P("$ko", s.子育支援一般); P("$kop", s.子育支援本人);
        P("$kk", s.厚生年金基金一般); P("$kkp", s.厚生年金基金本人);
        P("$ko1", s.雇用保険一般); P("$ko2", s.雇用保険一般以外);
        cmd.ExecuteNonQuery();
    }

    /// <summary>保険料額表を全行読み込む（No順）。</summary>
    public List<RateTableRow> LoadTable()
    {
        var list = new List<RateTableRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT No, 報酬月額_自, 報酬月額_至, 等級_短期, 等級_長期, 標準報酬月額_短期, 標準報酬月額_長期,
                   健康保険, 厚生年金, 介護保険, 子育支援, 厚生年金基金
            FROM T_料率保険料額表 ORDER BY No
            """;
        using var r = cmd.ExecuteReader();
        int? NI(int i) => r.IsDBNull(i) ? null : (int)r.GetInt64(i);
        double? ND(int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
        while (r.Read())
            list.Add(new RateTableRow
            {
                No = (int)r.GetInt64(0),
                報酬月額自 = NI(1), 報酬月額至 = NI(2), 等級短期 = NI(3), 等級長期 = NI(4),
                標準報酬月額短期 = NI(5), 標準報酬月額長期 = NI(6),
                健康保険 = ND(7), 厚生年金 = ND(8), 介護保険 = ND(9), 子育支援 = ND(10), 厚生年金基金 = ND(11),
            });
        return list;
    }

    /// <summary>保険料額表を保存（No単位でUPSERT）。</summary>
    public void SaveTable(IReadOnlyList<RateTableRow> rows)
    {
        using var tx = _conn.BeginTransaction();
        foreach (var row in rows)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO T_料率保険料額表
                    (No, 報酬月額_自, 報酬月額_至, 等級_短期, 等級_長期, 標準報酬月額_短期, 標準報酬月額_長期,
                     健康保険, 厚生年金, 介護保険, 子育支援, 厚生年金基金)
                VALUES ($no,$ja,$it,$ts,$tl,$hs,$hl,$kk,$kn,$kg,$ko,$kkk)
                ON CONFLICT(No) DO UPDATE SET
                    報酬月額_自=$ja, 報酬月額_至=$it, 等級_短期=$ts, 等級_長期=$tl,
                    標準報酬月額_短期=$hs, 標準報酬月額_長期=$hl,
                    健康保険=$kk, 厚生年金=$kn, 介護保険=$kg, 子育支援=$ko, 厚生年金基金=$kkk
                """;
            void P(string n, object? v) => cmd.Parameters.AddWithValue(n, v ?? (object)DBNull.Value);
            P("$no", row.No);
            P("$ja", row.報酬月額自); P("$it", row.報酬月額至);
            P("$ts", row.等級短期); P("$tl", row.等級長期);
            P("$hs", row.標準報酬月額短期); P("$hl", row.標準報酬月額長期);
            P("$kk", row.健康保険); P("$kn", row.厚生年金); P("$kg", row.介護保険);
            P("$ko", row.子育支援); P("$kkk", row.厚生年金基金);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
