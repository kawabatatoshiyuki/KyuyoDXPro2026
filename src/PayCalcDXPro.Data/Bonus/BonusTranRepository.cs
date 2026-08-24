using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.Bonus;

/// <summary>
/// 賞与トラン（T_賞与トラン）の読み書き。主キー＝Number＋回（同一年度内の賞与支給回 1〜4）。
/// 1行＝1従業員・1賞与回分。新規DBでは EnsureSchema で空テーブルを作るのみ（トランは取引データのためシードしない）。
/// 列順は現行 F_賞与処理 の INSERT に準拠（社保列＝健保→介護→子育支援→厚年→年金基金→小規模共済→雇用、の順。給与トランと順序が異なる点に注意）。
/// </summary>
public sealed class BonusTranRepository
{
    private readonly SqliteConnection _conn;

    public BonusTranRepository(SqliteConnection conn) => _conn = conn;

    // 賞与支給16＝手当項目１〜１２＋任意項目１〜４（DB列名・全角数字）。BonusEntry.賞与支給[0..15] に対応。
    private static readonly string[] 支給列 =
    {
        "手当項目１", "手当項目２", "手当項目３", "手当項目４", "手当項目５", "手当項目６",
        "手当項目７", "手当項目８", "手当項目９", "手当項目１０", "手当項目１１", "手当項目１２",
        "任意項目１", "任意項目２", "任意項目３", "任意項目４",
    };
    private static readonly string[] 控除列 =
    {
        "控除項目１", "控除項目２", "控除項目３", "控除項目４", "控除項目５",
        "控除項目６", "控除項目７", "控除項目８", "控除項目９",
    };

    public void EnsureSchema()
    {
        var cols = new List<string>
        {
            "\"Number\" TEXT", "\"回\" INTEGER", "\"賞与\" INTEGER",
        };
        cols.AddRange(支給列.Select(c => $"\"{c}\" INTEGER"));
        cols.AddRange(new[]
        {
            "\"総支給額\" INTEGER", "\"健康保険\" INTEGER", "\"介護保険\" INTEGER", "\"子育支援\" INTEGER",
            "\"厚生年金\" INTEGER", "\"年金基金\" INTEGER", "\"小規模共済\" INTEGER", "\"雇用保険\" INTEGER",
            "\"社会保険合計\" INTEGER", "\"課税対象額\" INTEGER", "\"所得税\" INTEGER",
        });
        cols.AddRange(控除列.Select(c => $"\"{c}\" INTEGER"));
        cols.AddRange(new[]
        {
            "\"総控除額\" INTEGER", "\"差引支給額\" INTEGER", "\"データ個数\" INTEGER", "\"扶養親族数\" INTEGER",
            "\"還付金額\" INTEGER", "\"徴収金額\" INTEGER", "\"備考\" TEXT",
        });
        Exec($"CREATE TABLE IF NOT EXISTS \"T_賞与トラン\" ({string.Join(", ", cols)}, PRIMARY KEY(\"Number\",\"回\"));");
    }

    /// <summary>指定従業員・回の1件を読み込む（無ければnull）。</summary>
    public BonusEntry? Load(string number, int 回)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns()} FROM \"T_賞与トラン\" WHERE Number=$n AND 回=$k LIMIT 1";
        cmd.Parameters.AddWithValue("$n", number);
        cmd.Parameters.AddWithValue("$k", 回);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>指定回の全従業員分を Number 順で読み込む。</summary>
    public List<BonusEntry> ListByRound(int 回)
    {
        var list = new List<BonusEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns()} FROM \"T_賞与トラン\" WHERE 回=$k ORDER BY CAST(Number AS INTEGER)";
        cmd.Parameters.AddWithValue("$k", 回);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    /// <summary>同一従業員・同一年度内で当回より前に支給済みの各回の総支給額を回順で返す（健保・介護の年度上限通算に使う）。</summary>
    public List<int> ListEarlierTotals(string number, int 回)
    {
        var list = new List<int>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT \"総支給額\" FROM \"T_賞与トラン\" WHERE Number=$n AND 回<$k ORDER BY 回";
        cmd.Parameters.AddWithValue("$n", number);
        cmd.Parameters.AddWithValue("$k", 回);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.IsDBNull(0) ? 0 : (int)r.GetInt64(0));
        return list;
    }

    /// <summary>1件をUPSERT（主キー一致で全列置換）。</summary>
    public void Save(BonusEntry e)
    {
        var fields = Fields(e);
        var colSql = string.Join(", ", fields.Select(f => $"\"{f.Col}\""));
        var prmSql = string.Join(", ", fields.Select((_, i) => $"$p{i}"));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"INSERT OR REPLACE INTO \"T_賞与トラン\" ({colSql}) VALUES ({prmSql})";
        for (int i = 0; i < fields.Count; i++)
            cmd.Parameters.AddWithValue($"$p{i}", fields[i].Value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>指定回で保存済み（行が存在する）の従業員Number一覧を返す。計算済みマーク用。</summary>
    public List<string> ListSavedNumbers(int 回)
    {
        var list = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT \"Number\" FROM \"T_賞与トラン\" WHERE 回=$k";
        cmd.Parameters.AddWithValue("$k", 回);
        using var r = cmd.ExecuteReader();
        while (r.Read()) if (!r.IsDBNull(0)) list.Add(r.GetString(0));
        return list;
    }

    public void Delete(string number, int 回)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"T_賞与トラン\" WHERE Number=$n AND 回=$k";
        cmd.Parameters.AddWithValue("$n", number);
        cmd.Parameters.AddWithValue("$k", 回);
        cmd.ExecuteNonQuery();
    }

    // ---- 列マッピング（保存・読込で同一順序）----
    private static List<(string Col, object? Value)> Fields(BonusEntry e)
    {
        var f = new List<(string, object?)>
        {
            ("Number", e.Number), ("回", e.回), ("賞与", e.賞与),
        };
        for (int i = 0; i < 16; i++) f.Add((支給列[i], e.賞与支給[i]));
        f.AddRange(new (string, object?)[]
        {
            ("総支給額", e.総支給額), ("健康保険", e.健康保険), ("介護保険", e.介護保険), ("子育支援", e.子育支援),
            ("厚生年金", e.厚生年金), ("年金基金", e.年金基金), ("小規模共済", e.小規模共済), ("雇用保険", e.雇用保険),
            ("社会保険合計", e.社会保険合計), ("課税対象額", e.課税対象額), ("所得税", e.所得税),
        });
        for (int i = 0; i < 9; i++) f.Add((控除列[i], e.控除項目[i]));
        f.AddRange(new (string, object?)[]
        {
            ("総控除額", e.総控除額), ("差引支給額", e.差引支給額), ("データ個数", e.データ個数), ("扶養親族数", e.扶養親族数),
            ("還付金額", e.還付金額), ("徴収金額", e.徴収金額), ("備考", e.備考),
        });
        return f;
    }

    private static string SelectColumns()
        => string.Join(", ", Fields(new BonusEntry()).Select(f => $"\"{f.Col}\""));

    private static BonusEntry Read(SqliteDataReader r)
    {
        int i = 0;
        int? I() => r.IsDBNull(i) ? null : (int)r.GetInt64(i);
        string? S() => r.IsDBNull(i) ? null : r.GetString(i);

        var e = new BonusEntry();
        e.Number = S() ?? ""; i++;
        e.回 = I() ?? 0; i++;
        e.賞与 = I(); i++;
        for (int k = 0; k < 16; k++) { e.賞与支給[k] = I(); i++; }
        e.総支給額 = I(); i++; e.健康保険 = I(); i++; e.介護保険 = I(); i++; e.子育支援 = I(); i++;
        e.厚生年金 = I(); i++; e.年金基金 = I(); i++; e.小規模共済 = I(); i++; e.雇用保険 = I(); i++;
        e.社会保険合計 = I(); i++; e.課税対象額 = I(); i++; e.所得税 = I(); i++;
        for (int k = 0; k < 9; k++) { e.控除項目[k] = I(); i++; }
        e.総控除額 = I(); i++; e.差引支給額 = I(); i++; e.データ個数 = I(); i++; e.扶養親族数 = I(); i++;
        e.還付金額 = I(); i++; e.徴収金額 = I(); i++; e.備考 = S(); i++;
        return e;
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
