using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.Payroll;

/// <summary>
/// 給与トラン（T_給与トラン）の読み書き。主キー＝Number＋支給回。
/// 1行＝1従業員・1支給回分。新規DBではEnsureSchemaで空テーブルを作るのみ（トランは取引データのためシードしない）。
/// </summary>
public sealed class PayrollTranRepository
{
    private readonly SqliteConnection _conn;

    public PayrollTranRepository(SqliteConnection conn) => _conn = conn;

    // 手当・控除の全角数字付き列名（DB準拠）。
    private static readonly string[] 手当列 =
    {
        "給与支給手当項目１", "給与支給手当項目２", "給与支給手当項目３", "給与支給手当項目４",
        "給与支給手当項目５", "給与支給手当項目６", "給与支給手当項目７", "給与支給手当項目８",
        "給与支給手当項目９", "給与支給手当項目１０", "給与支給手当項目１１", "給与支給手当項目１２",
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
            "\"Number\" TEXT", "\"支給回\" INTEGER", "\"基本給\" INTEGER",
        };
        cols.AddRange(手当列.Select(c => $"\"{c}\" INTEGER"));
        cols.AddRange(new[]
        {
            "\"時間外手当\" INTEGER", "\"勤怠控除\" INTEGER", "\"課税通勤費\" INTEGER", "\"非課税通勤費\" INTEGER",
            "\"総支給額\" INTEGER", "\"健康保険\" INTEGER", "\"介護保険\" INTEGER", "\"子育支援\" INTEGER",
            "\"小規模共済\" INTEGER", "\"厚生年金\" INTEGER", "\"年金基金\" INTEGER", "\"雇用保険\" INTEGER",
            "\"社会保険合計\" INTEGER", "\"課税対象額\" INTEGER", "\"所得税\" INTEGER", "\"住民税\" INTEGER",
        });
        cols.AddRange(控除列.Select(c => $"\"{c}\" INTEGER"));
        cols.AddRange(new[]
        {
            "\"総控除額\" INTEGER", "\"差引支給額\" INTEGER", "\"データ個数\" INTEGER", "\"扶養親族数\" INTEGER",
            "\"所定出勤日数\" REAL", "\"出勤日数\" REAL", "\"労働時間\" REAL",
            "\"欠勤日数_一般\" REAL", "\"欠勤日数_病気等\" REAL", "\"特別休暇\" REAL", "\"代休\" REAL",
            "\"有給休暇\" REAL", "\"有給残日数\" REAL", "\"遅刻早退\" REAL",
            "\"法定内残業\" REAL", "\"普通残業60h以下\" REAL", "\"深夜残業60h以下\" REAL",
            "\"休日出勤60h以下\" REAL", "\"休日深夜60h以下\" REAL", "\"普通残業60h超\" REAL",
            "\"深夜残業60h超\" REAL", "\"休日出勤60h超\" REAL", "\"休日深夜60h超\" REAL",
            "\"還付金額\" INTEGER", "\"徴収金額\" INTEGER", "\"備考\" TEXT", "\"非課税金額計\" INTEGER",
        });
        Exec($"CREATE TABLE IF NOT EXISTS \"T_給与トラン\" ({string.Join(", ", cols)}, PRIMARY KEY(\"Number\",\"支給回\"));");
    }

    /// <summary>指定従業員・支給回の1件を読み込む（無ければnull）。</summary>
    public PayrollEntry? Load(string number, int 支給回)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns()} FROM \"T_給与トラン\" WHERE Number=$n AND 支給回=$k LIMIT 1";
        cmd.Parameters.AddWithValue("$n", number);
        cmd.Parameters.AddWithValue("$k", 支給回);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>指定支給回（=支給月）の全従業員分を Number 順で読み込む。</summary>
    public List<PayrollEntry> ListByRound(int 支給回)
    {
        var list = new List<PayrollEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns()} FROM \"T_給与トラン\" WHERE 支給回=$k ORDER BY CAST(Number AS INTEGER)";
        cmd.Parameters.AddWithValue("$k", 支給回);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    /// <summary>1件をUPSERT（主キー一致で全列置換）。</summary>
    public void Save(PayrollEntry e)
    {
        var fields = Fields(e);
        var colSql = string.Join(", ", fields.Select(f => $"\"{f.Col}\""));
        var prmSql = string.Join(", ", fields.Select((_, i) => $"$p{i}"));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"INSERT OR REPLACE INTO \"T_給与トラン\" ({colSql}) VALUES ({prmSql})";
        for (int i = 0; i < fields.Count; i++)
            cmd.Parameters.AddWithValue($"$p{i}", fields[i].Value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>指定支給回で保存済み（行が存在する）の従業員Number一覧を返す。計算済みマーク用。</summary>
    public List<string> ListSavedNumbers(int 支給回)
    {
        var list = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT \"Number\" FROM \"T_給与トラン\" WHERE 支給回=$k";
        cmd.Parameters.AddWithValue("$k", 支給回);
        using var r = cmd.ExecuteReader();
        while (r.Read()) if (!r.IsDBNull(0)) list.Add(r.GetString(0));
        return list;
    }

    public void Delete(string number, int 支給回)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"T_給与トラン\" WHERE Number=$n AND 支給回=$k";
        cmd.Parameters.AddWithValue("$n", number);
        cmd.Parameters.AddWithValue("$k", 支給回);
        cmd.ExecuteNonQuery();
    }

    // ---- 列マッピング（保存・読込で同一順序）----
    private static List<(string Col, object? Value)> Fields(PayrollEntry e)
    {
        var f = new List<(string, object?)>
        {
            ("Number", e.Number), ("支給回", e.支給回), ("基本給", e.基本給),
        };
        for (int i = 0; i < 12; i++) f.Add((手当列[i], e.給与支給手当[i]));
        f.AddRange(new (string, object?)[]
        {
            ("時間外手当", e.時間外手当), ("勤怠控除", e.勤怠控除), ("課税通勤費", e.課税通勤費), ("非課税通勤費", e.非課税通勤費),
            ("総支給額", e.総支給額), ("健康保険", e.健康保険), ("介護保険", e.介護保険), ("子育支援", e.子育支援),
            ("小規模共済", e.小規模共済), ("厚生年金", e.厚生年金), ("年金基金", e.年金基金), ("雇用保険", e.雇用保険),
            ("社会保険合計", e.社会保険合計), ("課税対象額", e.課税対象額), ("所得税", e.所得税), ("住民税", e.住民税),
        });
        for (int i = 0; i < 9; i++) f.Add((控除列[i], e.控除項目[i]));
        f.AddRange(new (string, object?)[]
        {
            ("総控除額", e.総控除額), ("差引支給額", e.差引支給額), ("データ個数", e.データ個数), ("扶養親族数", e.扶養親族数),
            ("所定出勤日数", e.所定出勤日数), ("出勤日数", e.出勤日数), ("労働時間", e.労働時間),
            ("欠勤日数_一般", e.欠勤日数_一般), ("欠勤日数_病気等", e.欠勤日数_病気等), ("特別休暇", e.特別休暇), ("代休", e.代休),
            ("有給休暇", e.有給休暇), ("有給残日数", e.有給残日数), ("遅刻早退", e.遅刻早退),
            ("法定内残業", e.法定内残業), ("普通残業60h以下", e.普通残業60h以下), ("深夜残業60h以下", e.深夜残業60h以下),
            ("休日出勤60h以下", e.休日出勤60h以下), ("休日深夜60h以下", e.休日深夜60h以下), ("普通残業60h超", e.普通残業60h超),
            ("深夜残業60h超", e.深夜残業60h超), ("休日出勤60h超", e.休日出勤60h超), ("休日深夜60h超", e.休日深夜60h超),
            ("還付金額", e.還付金額), ("徴収金額", e.徴収金額), ("備考", e.備考), ("非課税金額計", e.非課税金額計),
        });
        return f;
    }

    private static string SelectColumns()
        => string.Join(", ", Fields(new PayrollEntry()).Select(f => $"\"{f.Col}\""));

    private static PayrollEntry Read(SqliteDataReader r)
    {
        int i = 0;
        int? I() => r.IsDBNull(i) ? null : (int)r.GetInt64(i);
        double? D() => r.IsDBNull(i) ? null : r.GetDouble(i);
        string? S() => r.IsDBNull(i) ? null : r.GetString(i);

        var e = new PayrollEntry();
        e.Number = S() ?? ""; i++;
        e.支給回 = I() ?? 0; i++;
        e.基本給 = I(); i++;
        for (int k = 0; k < 12; k++) { e.給与支給手当[k] = I(); i++; }
        e.時間外手当 = I(); i++; e.勤怠控除 = I(); i++; e.課税通勤費 = I(); i++; e.非課税通勤費 = I(); i++;
        e.総支給額 = I(); i++; e.健康保険 = I(); i++; e.介護保険 = I(); i++; e.子育支援 = I(); i++;
        e.小規模共済 = I(); i++; e.厚生年金 = I(); i++; e.年金基金 = I(); i++; e.雇用保険 = I(); i++;
        e.社会保険合計 = I(); i++; e.課税対象額 = I(); i++; e.所得税 = I(); i++; e.住民税 = I(); i++;
        for (int k = 0; k < 9; k++) { e.控除項目[k] = I(); i++; }
        e.総控除額 = I(); i++; e.差引支給額 = I(); i++; e.データ個数 = I(); i++; e.扶養親族数 = I(); i++;
        e.所定出勤日数 = D(); i++; e.出勤日数 = D(); i++; e.労働時間 = D(); i++;
        e.欠勤日数_一般 = D(); i++; e.欠勤日数_病気等 = D(); i++; e.特別休暇 = D(); i++; e.代休 = D(); i++;
        e.有給休暇 = D(); i++; e.有給残日数 = D(); i++; e.遅刻早退 = D(); i++;
        e.法定内残業 = D(); i++; e.普通残業60h以下 = D(); i++; e.深夜残業60h以下 = D(); i++;
        e.休日出勤60h以下 = D(); i++; e.休日深夜60h以下 = D(); i++; e.普通残業60h超 = D(); i++;
        e.深夜残業60h超 = D(); i++; e.休日出勤60h超 = D(); i++; e.休日深夜60h超 = D(); i++;
        e.還付金額 = I(); i++; e.徴収金額 = I(); i++; e.備考 = S(); i++; e.非課税金額計 = I(); i++;
        return e;
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
