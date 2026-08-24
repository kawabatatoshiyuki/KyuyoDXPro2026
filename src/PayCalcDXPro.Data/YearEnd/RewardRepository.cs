using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 報酬、料金、契約金及び賞金の支払調書（現行 T_年調報酬情報）の CRUD。
/// 現行 F_年調_報酬 の登録(MAX(No)+1)・更新・削除・一覧/参照に一致。
/// </summary>
public sealed class RewardRepository
{
    private readonly SqliteConnection _conn;

    public RewardRepository(SqliteConnection conn) => _conn = conn;

    public void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "T_年調報酬情報" (
              "No" INTEGER, "提出" TEXT, "名称" TEXT, "所在地" TEXT,
              "区分1" TEXT, "細目1" TEXT, "支払金額1" INTEGER, "内未払1" INTEGER, "源泉徴収税額1" INTEGER, "内未徴収税額1" INTEGER,
              "区分2" TEXT, "細目2" TEXT, "支払金額2" INTEGER, "内未払2" INTEGER, "源泉徴収税額2" INTEGER, "内未徴収税額2" INTEGER,
              "区分3" TEXT, "細目3" TEXT, "支払金額3" INTEGER, "内未払3" INTEGER, "源泉徴収税額3" INTEGER, "内未徴収税額3" INTEGER,
              "区分4" TEXT, "細目4" TEXT, "支払金額4" INTEGER, "内未払4" INTEGER, "源泉徴収税額4" INTEGER, "内未徴収税額4" INTEGER,
              "区分5" TEXT, "細目5" TEXT, "支払金額5" INTEGER, "内未払5" INTEGER, "源泉徴収税額5" INTEGER, "内未徴収税額5" INTEGER,
              "摘要" TEXT, "支払受者区分" TEXT, "支払受者番号" TEXT, "区分名称" TEXT,
              PRIMARY KEY("No"));
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>整理番号順の一覧（名称つき）。</summary>
    public IReadOnlyList<RewardListItem> List()
    {
        var list = new List<RewardListItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, IFNULL(名称,'') FROM \"T_年調報酬情報\" ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new RewardListItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public RewardRecord? Load(int no)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM \"T_年調報酬情報\" WHERE No=$n";
        cmd.Parameters.AddWithValue("$n", no);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        int? I(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? null : Convert.ToInt32(r.GetValue(o)); }
        string S(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? "" : Convert.ToString(r.GetValue(o)) ?? ""; }

        var e = new RewardRecord
        {
            No = no,
            提出 = S("提出") == "1",
            支払受者区分 = S("支払受者区分") == "2" ? "2" : "1",
            名称 = S("名称"), 所在地 = S("所在地"), 支払受者番号 = S("支払受者番号"),
            区分名称 = S("区分名称"), 摘要 = S("摘要"),
        };
        for (int i = 0; i < 5; i++)
        {
            e.区分[i] = S($"区分{i + 1}");
            e.細目[i] = S($"細目{i + 1}");
            e.支払金額[i] = I($"支払金額{i + 1}");
            e.内未払[i] = I($"内未払{i + 1}");
            e.源泉徴収税額[i] = I($"源泉徴収税額{i + 1}");
            e.内未徴収税額[i] = I($"内未徴収税額{i + 1}");
        }
        return e;
    }

    /// <summary>新規登録（No=MAX+1）。採番した No を返す。</summary>
    public int Insert(RewardRecord e)
    {
        using (var max = _conn.CreateCommand())
        {
            max.CommandText = "SELECT IFNULL(MAX(No),0)+1 FROM \"T_年調報酬情報\"";
            e.No = Convert.ToInt32(max.ExecuteScalar());
        }
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO \"T_年調報酬情報\" ({ColumnList}) VALUES ({ParamList})";
        Bind(cmd, e);
        cmd.ExecuteNonQuery();
        return e.No;
    }

    public void Update(RewardRecord e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE \"T_年調報酬情報\" SET {UpdateSet} WHERE No=$No";
        Bind(cmd, e);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int no)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"T_年調報酬情報\" WHERE No=$n";
        cmd.Parameters.AddWithValue("$n", no);
        cmd.ExecuteNonQuery();
    }

    /// <summary>年調報酬区分（コード＝1〜8号該当）。現行 M_コード「年調報酬区分」に一致。</summary>
    public static IReadOnlyList<RewardKubun> Kubuns { get; } =
        Enumerable.Range(1, 8).Select(i => new RewardKubun(i.ToString(), $"{i}号該当")).ToList();

    // ---- 列とパラメータ ----
    private static readonly string[] Cols = BuildCols();
    private static string[] BuildCols()
    {
        var cols = new List<string> { "No", "提出", "名称", "所在地" };
        for (int i = 1; i <= 5; i++)
            cols.AddRange(new[] { $"区分{i}", $"細目{i}", $"支払金額{i}", $"内未払{i}", $"源泉徴収税額{i}", $"内未徴収税額{i}" });
        cols.AddRange(new[] { "摘要", "支払受者区分", "支払受者番号", "区分名称" });
        return cols.ToArray();
    }
    private static string ColumnList => string.Join(", ", Cols.Select(c => $"\"{c}\""));
    private static string ParamList => string.Join(", ", Cols.Select(c => "$" + c));
    private static string UpdateSet => string.Join(", ", Cols.Where(c => c != "No").Select(c => $"\"{c}\"=$" + c));

    private static void Bind(SqliteCommand cmd, RewardRecord e)
    {
        void P(string c, object? v) => cmd.Parameters.AddWithValue("$" + c, v ?? DBNull.Value);
        P("No", e.No);
        P("提出", e.提出 ? "1" : "0");
        P("名称", e.名称); P("所在地", e.所在地);
        for (int i = 0; i < 5; i++)
        {
            P($"区分{i + 1}", e.区分[i] ?? "");
            P($"細目{i + 1}", e.細目[i] ?? "");
            P($"支払金額{i + 1}", e.支払金額[i]);
            P($"内未払{i + 1}", e.内未払[i]);
            P($"源泉徴収税額{i + 1}", e.源泉徴収税額[i]);
            P($"内未徴収税額{i + 1}", e.内未徴収税額[i]);
        }
        P("摘要", e.摘要);
        P("支払受者区分", e.支払受者区分 == "2" ? "2" : "1");
        P("支払受者番号", e.支払受者番号);
        P("区分名称", e.区分名称);
    }
}
