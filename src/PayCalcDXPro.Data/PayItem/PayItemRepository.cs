using System.Text;
using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.PayItem;

/// <summary>
/// 支給・控除項目設定（M_支給控除項目設定、1レコード レコード番号=1）の読み書き。
/// 固定項目（基本給・各社会保険ラベル等）はDEFAULTを持ち本画面では変更しない。
/// </summary>
public sealed class PayItemRepository
{
    private readonly SqliteConnection _conn;
    private const int レコード番号 = 1;

    public PayItemRepository(SqliteConnection conn) => _conn = conn;

    private static string FW(int n)
    {
        var sb = new StringBuilder();
        foreach (char c in n.ToString()) sb.Append((char)(c - '0' + '０'));
        return sb.ToString();
    }

    // 固定項目（列名→既定ラベル）。表示は読み取り専用。
    private static readonly (string col, string def)[] Fixed =
    {
        ("基本給","基本給"), ("時間外手当","時間外手当"), ("勤怠控除","勤怠控除"),
        ("課税通勤費","課税通勤費"), ("非課税通勤費","非課税通勤費"),
        ("給与総支給額","総支給額"), ("給与健康保険","健康保険"), ("給与介護保険","介護保険"),
        ("給与子育支援","子育支援"), ("給与小規模共済","小規模共済"), ("給与厚生年金","厚生年金"),
        ("給与年金基金","年金基金"), ("給与雇用保険","雇用保険"), ("給与社会保険合計","社会保険合計"),
        ("給与課税対象額","課税対象額"), ("給与所得税","所得税"), ("給与住民税","住民税"),
        ("総控除額","総控除額"), ("給与控除差引支給額","差引支給額"),
        ("賞与","賞与"), ("賞与総支給額","総支給額"), ("賞与健康保険","健康保険"),
        ("賞与介護保険","介護保険"), ("賞与子育支援","子育支援"), ("賞与小規模共済","小規模共済"),
        ("賞与厚生年金","厚生年金"), ("賞与年金基金","年金基金"), ("賞与雇用保険","雇用保険"),
        ("賞与社会保険合計","社会保険合計"), ("賞与課税対象額","課税対象額"), ("賞与所得税","所得税"),
        ("賞与総控除額","総控除額"), ("賞与控除差引支給額","差引支給額"),
    };

    public void EnsureSchema()
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE IF NOT EXISTS \"M_支給控除項目設定\" (\"レコード番号\" INTEGER");
        foreach (var (col, def) in Fixed)
            sb.Append($", \"{col}\" TEXT DEFAULT '{def}'");
        for (int i = 1; i <= 12; i++)
            sb.Append($", \"給与支給手当項目{FW(i)}\" TEXT, \"給与支給非課税区分{FW(i)}\" TEXT");
        for (int i = 1; i <= 9; i++) sb.Append($", \"給与控除項目{FW(i)}\" TEXT");
        for (int i = 1; i <= 16; i++) sb.Append($", \"賞与手当項目{FW(i)}\" TEXT");
        for (int i = 1; i <= 9; i++) sb.Append($", \"賞与控除項目{FW(i)}\" TEXT");
        sb.Append(", \"項目名印字フラグ\" TEXT");
        sb.Append(", PRIMARY KEY(\"レコード番号\"))");

        Exec(sb.ToString());

        // レコードが無ければ既定行を作成（固定項目はDEFAULTで埋まる）。
        using var chk = _conn.CreateCommand();
        chk.CommandText = "SELECT COUNT(*) FROM M_支給控除項目設定 WHERE レコード番号 = $r";
        chk.Parameters.AddWithValue("$r", レコード番号);
        if (Convert.ToInt64(chk.ExecuteScalar()) == 0)
            Exec($"INSERT INTO M_支給控除項目設定 (レコード番号) VALUES ({レコード番号})");
    }

    public PayItemSettings Load()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM M_支給控除項目設定 WHERE レコード番号 = $r LIMIT 1";
        cmd.Parameters.AddWithValue("$r", レコード番号);
        using var r = cmd.ExecuteReader();

        var s = new PayItemSettings();
        if (!r.Read()) return Padded(s);

        string S(string col)
        {
            int o = r.GetOrdinal(col);
            return r.IsDBNull(o) ? "" : r.GetValue(o).ToString() ?? "";
        }

        for (int i = 1; i <= 12; i++)
            s.給与手当.Add(new AllowanceItem { 名称 = S($"給与支給手当項目{FW(i)}"), 非課税 = S($"給与支給非課税区分{FW(i)}") == "1" });
        for (int i = 1; i <= 9; i++) s.給与控除.Add(new ItemName { 名称 = S($"給与控除項目{FW(i)}") });
        for (int i = 1; i <= 16; i++) s.賞与手当.Add(new ItemName { 名称 = S($"賞与手当項目{FW(i)}") });
        for (int i = 1; i <= 9; i++) s.賞与控除.Add(new ItemName { 名称 = S($"賞与控除項目{FW(i)}") });
        s.項目名印字 = S("項目名印字フラグ") == "1";

        foreach (var (col, _) in Fixed) s.固定ラベル[col] = S(col);
        return s;
    }

    public void Save(PayItemSettings s)
    {
        var pairs = new List<(string col, object? val)>();
        for (int i = 0; i < 12; i++)
        {
            var a = i < s.給与手当.Count ? s.給与手当[i] : new AllowanceItem();
            pairs.Add(($"給与支給手当項目{FW(i + 1)}", NullIfBlank(a.名称)));
            pairs.Add(($"給与支給非課税区分{FW(i + 1)}", a.非課税 ? "1" : "0"));
        }
        for (int i = 0; i < 9; i++)
            pairs.Add(($"給与控除項目{FW(i + 1)}", NullIfBlank(Get(s.給与控除, i))));
        for (int i = 0; i < 16; i++)
            pairs.Add(($"賞与手当項目{FW(i + 1)}", NullIfBlank(Get(s.賞与手当, i))));
        for (int i = 0; i < 9; i++)
            pairs.Add(($"賞与控除項目{FW(i + 1)}", NullIfBlank(Get(s.賞与控除, i))));
        pairs.Add(("項目名印字フラグ", s.項目名印字 ? "1" : "0"));

        var set = string.Join(", ", pairs.Select((x, i) => $"\"{x.col}\" = $p{i}"));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE M_支給控除項目設定 SET {set} WHERE レコード番号 = $r";
        cmd.Parameters.AddWithValue("$r", レコード番号);
        for (int i = 0; i < pairs.Count; i++)
            cmd.Parameters.AddWithValue($"$p{i}", pairs[i].val ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static string Get(List<ItemName> list, int i) => i < list.Count ? list[i].名称 : "";
    private static string? NullIfBlank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v!.Trim();

    private static PayItemSettings Padded(PayItemSettings s)
    {
        while (s.給与手当.Count < 12) s.給与手当.Add(new AllowanceItem());
        while (s.給与控除.Count < 9) s.給与控除.Add(new ItemName());
        while (s.賞与手当.Count < 16) s.賞与手当.Add(new ItemName());
        while (s.賞与控除.Count < 9) s.賞与控除.Add(new ItemName());
        return s;
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
