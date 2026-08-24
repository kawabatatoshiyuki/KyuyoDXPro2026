using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 不動産等の売買又は貸付けのあっせん手数料の支払調書（現行 T_不動産あっせん情報）の CRUD。
/// 現行 F_年調_不動産あっせん の登録(MAX(No)+1)・更新・削除・一覧/参照に一致。
/// </summary>
public sealed class RealEstateMediationRepository
{
    private readonly SqliteConnection _conn;

    public RealEstateMediationRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>区分の候補（現行 cbKubun1〜2。編集可＝自由入力も許容）。</summary>
    public static IReadOnlyList<string> 区分候補 { get; } = new[]
    { "譲渡", "譲受け", "貸付け", "借受け" };

    /// <summary>物件の種類区分の候補（現行 cmb_Bukken1〜2。編集可）。</summary>
    public static IReadOnlyList<string> 物件の種類候補 { get; } = new[]
    { "土地", "借地権", "地役権", "建物" };

    public void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "T_不動産あっせん情報" (
              "No" INTEGER, "不要" TEXT, "名称" TEXT, "所在地" TEXT,
              "区分1" TEXT, "取得年月日1" TEXT, "支払金額1" INTEGER,
              "物件の種類区分1" TEXT, "物件の所在地1" TEXT, "数量1" TEXT, "取得金額1" INTEGER,
              "区分2" TEXT, "取得年月日2" TEXT, "支払金額2" INTEGER,
              "物件の種類区分2" TEXT, "物件の所在地2" TEXT, "数量2" TEXT, "取得金額2" INTEGER,
              "摘要1" TEXT, "摘要2" TEXT, "摘要3" TEXT,
              "支払受者区分" TEXT, "支払受者番号" TEXT,
              PRIMARY KEY("No"));
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>整理番号順の一覧（名称つき）。</summary>
    public IReadOnlyList<RealEstateMediationListItem> List()
    {
        var list = new List<RealEstateMediationListItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, IFNULL(名称,'') FROM \"T_不動産あっせん情報\" ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new RealEstateMediationListItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public RealEstateMediationRecord? Load(int no)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM \"T_不動産あっせん情報\" WHERE No=$n";
        cmd.Parameters.AddWithValue("$n", no);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        int? I(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? null : Convert.ToInt32(r.GetValue(o)); }
        string S(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? "" : Convert.ToString(r.GetValue(o)) ?? ""; }

        var e = new RealEstateMediationRecord
        {
            No = no,
            不要 = S("不要") == "1",
            名称 = S("名称"), 所在地 = S("所在地"),
            支払受者区分 = S("支払受者区分") == "2" ? "2" : "1", 支払受者番号 = S("支払受者番号"),
        };
        for (int i = 0; i < 2; i++)
        {
            e.区分[i] = S($"区分{i + 1}");
            e.取得年月日[i] = ParseDate(S($"取得年月日{i + 1}"));
            e.支払金額[i] = I($"支払金額{i + 1}");
            e.物件の種類区分[i] = S($"物件の種類区分{i + 1}");
            e.物件の所在地[i] = S($"物件の所在地{i + 1}");
            e.数量[i] = S($"数量{i + 1}");
            e.取得金額[i] = I($"取得金額{i + 1}");
        }
        e.摘要[0] = S("摘要1");
        e.摘要[1] = S("摘要2");
        e.摘要[2] = S("摘要3");
        return e;
    }

    /// <summary>新規登録（No=MAX+1）。採番した No を返す。</summary>
    public int Insert(RealEstateMediationRecord e)
    {
        using (var max = _conn.CreateCommand())
        {
            max.CommandText = "SELECT IFNULL(MAX(No),0)+1 FROM \"T_不動産あっせん情報\"";
            e.No = Convert.ToInt32(max.ExecuteScalar());
        }
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO \"T_不動産あっせん情報\" ({ColumnList}) VALUES ({ParamList})";
        Bind(cmd, e);
        cmd.ExecuteNonQuery();
        return e.No;
    }

    public void Update(RealEstateMediationRecord e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE \"T_不動産あっせん情報\" SET {UpdateSet} WHERE No=$No";
        Bind(cmd, e);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int no)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"T_不動産あっせん情報\" WHERE No=$n";
        cmd.Parameters.AddWithValue("$n", no);
        cmd.ExecuteNonQuery();
    }

    // 日付は ISO("yyyy-MM-dd") で保存し、読み出し時に緩やかにパースする。
    private static DateTime? ParseDate(string s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static string? FormatDate(DateTime? d)
        => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---- 列とパラメータ ----
    private static readonly string[] Cols = BuildCols();
    private static string[] BuildCols()
    {
        var cols = new List<string> { "No", "不要", "名称", "所在地" };
        for (int i = 1; i <= 2; i++)
            cols.AddRange(new[]
            {
                $"区分{i}", $"取得年月日{i}", $"支払金額{i}",
                $"物件の種類区分{i}", $"物件の所在地{i}", $"数量{i}", $"取得金額{i}",
            });
        cols.AddRange(new[] { "摘要1", "摘要2", "摘要3", "支払受者区分", "支払受者番号" });
        return cols.ToArray();
    }
    private static string ColumnList => string.Join(", ", Cols.Select(c => $"\"{c}\""));
    private static string ParamList => string.Join(", ", Cols.Select(c => "$" + c));
    private static string UpdateSet => string.Join(", ", Cols.Where(c => c != "No").Select(c => $"\"{c}\"=$" + c));

    private static void Bind(SqliteCommand cmd, RealEstateMediationRecord e)
    {
        void P(string c, object? v) => cmd.Parameters.AddWithValue("$" + c, v ?? DBNull.Value);
        P("No", e.No);
        P("不要", e.不要 ? "1" : "0");
        P("名称", e.名称); P("所在地", e.所在地);
        for (int i = 0; i < 2; i++)
        {
            P($"区分{i + 1}", e.区分[i] ?? "");
            P($"取得年月日{i + 1}", FormatDate(e.取得年月日[i]));
            P($"支払金額{i + 1}", e.支払金額[i]);
            P($"物件の種類区分{i + 1}", e.物件の種類区分[i] ?? "");
            P($"物件の所在地{i + 1}", e.物件の所在地[i] ?? "");
            P($"数量{i + 1}", e.数量[i] ?? "");
            P($"取得金額{i + 1}", e.取得金額[i]);
        }
        P("摘要1", e.摘要[0] ?? ""); P("摘要2", e.摘要[1] ?? ""); P("摘要3", e.摘要[2] ?? "");
        P("支払受者区分", e.支払受者区分 == "2" ? "2" : "1");
        P("支払受者番号", e.支払受者番号);
    }
}
