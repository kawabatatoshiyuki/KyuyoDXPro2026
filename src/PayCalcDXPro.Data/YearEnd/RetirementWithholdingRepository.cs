using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 退職所得の源泉徴収票・特別徴収票（現行 T_退職源泉情報）の CRUD。
/// 現行 F_年調_退職源泉 の登録(MAX(No)+1)・更新・削除・一覧/参照に一致。
/// </summary>
public sealed class RetirementWithholdingRepository
{
    private readonly SqliteConnection _conn;

    public RetirementWithholdingRepository(SqliteConnection conn) => _conn = conn;

    public void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "T_退職源泉情報" (
              "No" INTEGER, "提出" TEXT, "氏名" TEXT, "個人番号" TEXT, "住所" TEXT, "年始住所" TEXT,
              "支払金額1" INTEGER, "源泉徴収税額1" INTEGER, "市町村民税1" INTEGER, "道府県民税1" INTEGER,
              "支払金額2" INTEGER, "源泉徴収税額2" INTEGER, "市町村民税2" INTEGER, "道府県民税2" INTEGER,
              "支払金額3" INTEGER, "源泉徴収税額3" INTEGER, "市町村民税3" INTEGER, "道府県民税3" INTEGER,
              "退職所得控除額" INTEGER, "勤続年数" TEXT, "就職年月日" TEXT, "退職年月日" TEXT, "摘要" TEXT,
              PRIMARY KEY("No"));
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>整理番号順の一覧（氏名つき）。</summary>
    public IReadOnlyList<RetirementWithholdingListItem> List()
    {
        var list = new List<RetirementWithholdingListItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, IFNULL(氏名,'') FROM \"T_退職源泉情報\" ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new RetirementWithholdingListItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public RetirementWithholdingRecord? Load(int no)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM \"T_退職源泉情報\" WHERE No=$n";
        cmd.Parameters.AddWithValue("$n", no);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        int I(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); }
        string S(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? "" : Convert.ToString(r.GetValue(o)) ?? ""; }

        var e = new RetirementWithholdingRecord
        {
            No = no,
            提出 = S("提出") == "1",
            氏名 = S("氏名"), 個人番号 = S("個人番号"), 住所 = S("住所"), 年始住所 = S("年始住所"),
            退職所得控除額 = I("退職所得控除額"),
            勤続年数 = S("勤続年数"), 就職年月日 = S("就職年月日"), 退職年月日 = S("退職年月日"),
            摘要 = S("摘要"),
        };
        for (int i = 0; i < 3; i++)
        {
            e.支払金額[i] = I($"支払金額{i + 1}");
            e.源泉徴収税額[i] = I($"源泉徴収税額{i + 1}");
            e.市町村民税[i] = I($"市町村民税{i + 1}");
            e.道府県民税[i] = I($"道府県民税{i + 1}");
        }
        return e;
    }

    /// <summary>新規登録（No=MAX+1）。採番した No を返す。</summary>
    public int Insert(RetirementWithholdingRecord e)
    {
        int no;
        using (var max = _conn.CreateCommand())
        {
            max.CommandText = "SELECT IFNULL(MAX(No),0)+1 FROM \"T_退職源泉情報\"";
            no = Convert.ToInt32(max.ExecuteScalar());
        }
        e.No = no;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO \"T_退職源泉情報\" ({ColumnList}) VALUES ({ParamList})";
        Bind(cmd, e);
        cmd.ExecuteNonQuery();
        return no;
    }

    public void Update(RetirementWithholdingRecord e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE \"T_退職源泉情報\" SET {UpdateSet} WHERE No=$No";
        Bind(cmd, e);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int no)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"T_退職源泉情報\" WHERE No=$n";
        cmd.Parameters.AddWithValue("$n", no);
        cmd.ExecuteNonQuery();
    }

    // ---- 列とパラメータ ----
    private static readonly string[] Cols =
    {
        "No","提出","氏名","個人番号","住所","年始住所",
        "支払金額1","源泉徴収税額1","市町村民税1","道府県民税1",
        "支払金額2","源泉徴収税額2","市町村民税2","道府県民税2",
        "支払金額3","源泉徴収税額3","市町村民税3","道府県民税3",
        "退職所得控除額","勤続年数","就職年月日","退職年月日","摘要",
    };
    private static string ColumnList => string.Join(", ", Cols.Select(c => $"\"{c}\""));
    private static string ParamList => string.Join(", ", Cols.Select(c => "$" + c));
    private static string UpdateSet => string.Join(", ", Cols.Where(c => c != "No").Select(c => $"\"{c}\"=$" + c));

    private static void Bind(SqliteCommand cmd, RetirementWithholdingRecord e)
    {
        void P(string c, object? v) => cmd.Parameters.AddWithValue("$" + c, v ?? DBNull.Value);
        P("No", e.No);
        P("提出", e.提出 ? "1" : "0");
        P("氏名", e.氏名); P("個人番号", e.個人番号); P("住所", e.住所); P("年始住所", e.年始住所);
        for (int i = 0; i < 3; i++)
        {
            P($"支払金額{i + 1}", e.支払金額[i]);
            P($"源泉徴収税額{i + 1}", e.源泉徴収税額[i]);
            P($"市町村民税{i + 1}", e.市町村民税[i]);
            P($"道府県民税{i + 1}", e.道府県民税[i]);
        }
        P("退職所得控除額", e.退職所得控除額);
        P("勤続年数", e.勤続年数); P("就職年月日", e.就職年月日); P("退職年月日", e.退職年月日);
        P("摘要", e.摘要);
    }
}
