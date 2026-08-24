using System.Text;
using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.PayDay;

/// <summary>
/// 支払日情報（M_支給日情報）の読み書き。1社1レコード（会社番号=1）。
/// 月別12件＋賞与4件。処理済みフラグは給与/賞与処理が更新するため画面保存では変更しない。
/// </summary>
public sealed class PayDayRepository
{
    private readonly SqliteConnection _conn;
    private const int 会社番号 = 1;

    public PayDayRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>全角数字（1→"１", 12→"１２"）。現行の列名に合わせるため。</summary>
    private static string FW(int n)
    {
        var sb = new StringBuilder();
        foreach (char c in n.ToString()) sb.Append((char)(c - '0' + '０'));
        return sb.ToString();
    }

    public void EnsureSchema()
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE IF NOT EXISTS \"M_支給日情報\" (");
        sb.Append("\"会社番号\" INTEGER, \"支給年\" INTEGER, \"支給日\" INTEGER, \"締月\" TEXT, \"締日付\" TEXT");
        for (int i = 1; i <= 12; i++)
        {
            string f = FW(i);
            sb.Append($", \"支給月{f}\" INTEGER, \"支給日{f}\" INTEGER, \"対象年{f}\" INTEGER, ");
            sb.Append($"\"対象月{f}\" INTEGER, \"所定勤務日数{f}\" INTEGER, \"処理済みフラグ{f}\" TEXT");
        }
        for (int i = 1; i <= 4; i++)
        {
            string f = FW(i);
            sb.Append($", \"{f}回目賞与支給月\" INTEGER, \"{f}回目賞与支給日\" INTEGER, \"賞与処理済みフラグ{f}\" TEXT");
        }
        sb.Append(", PRIMARY KEY(\"会社番号\"))");

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.ExecuteNonQuery();
    }

    /// <summary>1件読み込む。未登録なら既定値（12月＋4賞与の空行）を返す。</summary>
    public PayDayInfo Load()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM M_支給日情報 WHERE 会社番号 = $c LIMIT 1";
        cmd.Parameters.AddWithValue("$c", 会社番号);
        using var r = cmd.ExecuteReader();

        var info = new PayDayInfo();
        bool found = r.Read();

        int? NI(string col)
        {
            int o = r.GetOrdinal(col);
            return r.IsDBNull(o) ? null : (int)r.GetInt64(o);
        }
        string NS(string col)
        {
            int o = r.GetOrdinal(col);
            return r.IsDBNull(o) ? "" : r.GetValue(o).ToString() ?? "";
        }

        if (found)
        {
            info.支給年 = NI("支給年") ?? DateTime.Now.Year;
            info.支給日 = NI("支給日");
            info.締月 = NS("締月");
            info.締日付 = NS("締日付");
        }

        for (int i = 1; i <= 12; i++)
        {
            string f = FW(i);
            info.月別.Add(found
                ? new MonthlyPayDay
                {
                    支給月 = NI($"支給月{f}"), 支給日 = NI($"支給日{f}"),
                    対象年 = NI($"対象年{f}"), 対象月 = NI($"対象月{f}"),
                    所定勤務日数 = NI($"所定勤務日数{f}"), 処理済みフラグ = NS($"処理済みフラグ{f}"),
                }
                : new MonthlyPayDay());
        }
        for (int i = 1; i <= 4; i++)
        {
            string f = FW(i);
            info.賞与.Add(found
                ? new BonusPayDay
                {
                    支給月 = NI($"{f}回目賞与支給月"), 支給日 = NI($"{f}回目賞与支給日"),
                    処理済みフラグ = NS($"賞与処理済みフラグ{f}"),
                }
                : new BonusPayDay());
        }
        return info;
    }

    /// <summary>指定支給回の月別 処理済みフラグを立てる（現行 給与処理 保存時の M_支給日情報 更新に対応）。</summary>
    public void MarkProcessed(int 支給回)
    {
        if (支給回 < 1 || 支給回 > 12) return;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE M_支給日情報 SET \"処理済みフラグ{FW(支給回)}\" = '1' WHERE 会社番号 = $c";
        cmd.Parameters.AddWithValue("$c", 会社番号);
        cmd.ExecuteNonQuery();
    }

    /// <summary>保存（無ければINSERT、あればUPDATE）。処理済みフラグ列は対象外（保全）。</summary>
    public void Save(PayDayInfo p)
    {
        // 編集対象の (列名, 値) を組み立てる（処理済みフラグは含めない）。
        var pairs = new List<(string col, object? val)>
        {
            ("支給年", p.支給年),
            ("支給日", p.支給日),
            ("締月", p.締月 ?? ""),
            ("締日付", p.締日付 ?? ""),
        };
        for (int i = 0; i < 12; i++)
        {
            string f = FW(i + 1);
            var m = i < p.月別.Count ? p.月別[i] : new MonthlyPayDay();
            pairs.Add(($"支給月{f}", m.支給月));
            pairs.Add(($"支給日{f}", m.支給日));
            pairs.Add(($"対象年{f}", m.対象年));
            pairs.Add(($"対象月{f}", m.対象月));
            pairs.Add(($"所定勤務日数{f}", m.所定勤務日数));
        }
        for (int i = 0; i < 4; i++)
        {
            string f = FW(i + 1);
            var b = i < p.賞与.Count ? p.賞与[i] : new BonusPayDay();
            pairs.Add(($"{f}回目賞与支給月", b.支給月));
            pairs.Add(($"{f}回目賞与支給日", b.支給日));
        }

        bool exists;
        using (var chk = _conn.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(*) FROM M_支給日情報 WHERE 会社番号 = $c";
            chk.Parameters.AddWithValue("$c", 会社番号);
            exists = Convert.ToInt64(chk.ExecuteScalar()) > 0;
        }

        using var cmd = _conn.CreateCommand();
        if (exists)
        {
            var set = string.Join(", ", pairs.Select((x, i) => $"\"{x.col}\" = $p{i}"));
            cmd.CommandText = $"UPDATE M_支給日情報 SET {set} WHERE 会社番号 = $c";
        }
        else
        {
            var cols = string.Join(", ", new[] { "会社番号" }.Concat(pairs.Select(x => $"\"{x.col}\"")));
            var vals = string.Join(", ", new[] { "$c" }.Concat(pairs.Select((_, i) => $"$p{i}")));
            cmd.CommandText = $"INSERT INTO M_支給日情報 ({cols}) VALUES ({vals})";
        }
        cmd.Parameters.AddWithValue("$c", 会社番号);
        for (int i = 0; i < pairs.Count; i++)
            cmd.Parameters.AddWithValue($"$p{i}", pairs[i].val ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
