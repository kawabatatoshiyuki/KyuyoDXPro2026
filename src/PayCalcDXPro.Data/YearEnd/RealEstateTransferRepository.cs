using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 不動産等の譲受けの対価の支払調書（現行 T_不動産譲受）の CRUD。
/// 現行 F_年調_不動産譲受 の登録(MAX(No)+1)・更新・削除・一覧/参照に一致。
/// </summary>
public sealed class RealEstateTransferRepository
{
    private readonly SqliteConnection _conn;

    public RealEstateTransferRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>物件の種類の候補（現行 F_年調_不動産譲受 の ComboBox1〜3。編集可＝自由入力も許容）。</summary>
    public static IReadOnlyList<string> 物件の種類候補 { get; } = new[]
    { "地代", "借地権", "建物", "船舶", "航空機" };

    public void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "T_不動産譲受" (
              "No" INTEGER, "不要" TEXT, "名称" TEXT, "所在地" TEXT,
              "物件の種類1" TEXT, "物件の所在地1" TEXT, "細目1" TEXT, "数量1" TEXT, "取得年月日1" TEXT, "支払金額1" INTEGER,
              "物件の種類2" TEXT, "物件の所在地2" TEXT, "細目2" TEXT, "数量2" TEXT, "取得年月日2" TEXT, "支払金額2" INTEGER,
              "物件の種類3" TEXT, "物件の所在地3" TEXT, "細目3" TEXT, "数量3" TEXT, "取得年月日3" TEXT, "支払金額3" INTEGER,
              "摘要1" TEXT, "摘要2" TEXT,
              "あっせん名称" TEXT, "あっせん所在地" TEXT, "支払確定年月日" TEXT, "あっせん手数料" INTEGER,
              "支払受者区分" TEXT, "支払受者番号" TEXT, "あっせん者区分" TEXT, "あっせん者番号" TEXT,
              PRIMARY KEY("No"));
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>整理番号順の一覧（名称つき）。</summary>
    public IReadOnlyList<RealEstateTransferListItem> List()
    {
        var list = new List<RealEstateTransferListItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT No, IFNULL(名称,'') FROM \"T_不動産譲受\" ORDER BY No";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new RealEstateTransferListItem(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    public RealEstateTransferRecord? Load(int no)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM \"T_不動産譲受\" WHERE No=$n";
        cmd.Parameters.AddWithValue("$n", no);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        int? I(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? null : Convert.ToInt32(r.GetValue(o)); }
        string S(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? "" : Convert.ToString(r.GetValue(o)) ?? ""; }

        var e = new RealEstateTransferRecord
        {
            No = no,
            不要 = S("不要") == "1",
            名称 = S("名称"), 所在地 = S("所在地"),
            支払受者区分 = S("支払受者区分") == "2" ? "2" : "1", 支払受者番号 = S("支払受者番号"),
            あっせん名称 = S("あっせん名称"), あっせん所在地 = S("あっせん所在地"),
            支払確定年月日 = ParseDate(S("支払確定年月日")),
            あっせん手数料 = I("あっせん手数料"),
            あっせん者区分 = S("あっせん者区分") == "2" ? "2" : "1", あっせん者番号 = S("あっせん者番号"),
        };
        for (int i = 0; i < 3; i++)
        {
            e.物件の種類[i] = S($"物件の種類{i + 1}");
            e.物件の所在地[i] = S($"物件の所在地{i + 1}");
            e.細目[i] = S($"細目{i + 1}");
            e.数量[i] = S($"数量{i + 1}");
            e.取得年月日[i] = ParseDate(S($"取得年月日{i + 1}"));
            e.支払金額[i] = I($"支払金額{i + 1}");
        }
        e.摘要[0] = S("摘要1");
        e.摘要[1] = S("摘要2");
        return e;
    }

    /// <summary>新規登録（No=MAX+1）。採番した No を返す。</summary>
    public int Insert(RealEstateTransferRecord e)
    {
        using (var max = _conn.CreateCommand())
        {
            max.CommandText = "SELECT IFNULL(MAX(No),0)+1 FROM \"T_不動産譲受\"";
            e.No = Convert.ToInt32(max.ExecuteScalar());
        }
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO \"T_不動産譲受\" ({ColumnList}) VALUES ({ParamList})";
        Bind(cmd, e);
        cmd.ExecuteNonQuery();
        return e.No;
    }

    public void Update(RealEstateTransferRecord e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE \"T_不動産譲受\" SET {UpdateSet} WHERE No=$No";
        Bind(cmd, e);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int no)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"T_不動産譲受\" WHERE No=$n";
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
        for (int i = 1; i <= 3; i++)
            cols.AddRange(new[] { $"物件の種類{i}", $"物件の所在地{i}", $"細目{i}", $"数量{i}", $"取得年月日{i}", $"支払金額{i}" });
        cols.AddRange(new[] { "摘要1", "摘要2", "あっせん名称", "あっせん所在地", "支払確定年月日", "あっせん手数料",
            "支払受者区分", "支払受者番号", "あっせん者区分", "あっせん者番号" });
        return cols.ToArray();
    }
    private static string ColumnList => string.Join(", ", Cols.Select(c => $"\"{c}\""));
    private static string ParamList => string.Join(", ", Cols.Select(c => "$" + c));
    private static string UpdateSet => string.Join(", ", Cols.Where(c => c != "No").Select(c => $"\"{c}\"=$" + c));

    private static void Bind(SqliteCommand cmd, RealEstateTransferRecord e)
    {
        void P(string c, object? v) => cmd.Parameters.AddWithValue("$" + c, v ?? DBNull.Value);
        P("No", e.No);
        P("不要", e.不要 ? "1" : "0");
        P("名称", e.名称); P("所在地", e.所在地);
        for (int i = 0; i < 3; i++)
        {
            P($"物件の種類{i + 1}", e.物件の種類[i] ?? "");
            P($"物件の所在地{i + 1}", e.物件の所在地[i] ?? "");
            P($"細目{i + 1}", e.細目[i] ?? "");
            P($"数量{i + 1}", e.数量[i] ?? "");
            P($"取得年月日{i + 1}", FormatDate(e.取得年月日[i]));
            P($"支払金額{i + 1}", e.支払金額[i]);
        }
        P("摘要1", e.摘要[0] ?? ""); P("摘要2", e.摘要[1] ?? "");
        P("あっせん名称", e.あっせん名称); P("あっせん所在地", e.あっせん所在地);
        P("支払確定年月日", FormatDate(e.支払確定年月日));
        P("あっせん手数料", e.あっせん手数料);
        P("支払受者区分", e.支払受者区分 == "2" ? "2" : "1");
        P("支払受者番号", e.支払受者番号);
        P("あっせん者区分", e.あっせん者区分 == "2" ? "2" : "1");
        P("あっせん者番号", e.あっせん者番号);
    }
}
