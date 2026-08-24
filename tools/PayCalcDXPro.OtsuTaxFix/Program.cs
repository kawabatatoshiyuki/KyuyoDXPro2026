using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Services;

// 乙欄（従たる給与・賞与）の源泉税表がブラウザ版DBに無い/空だと、乙欄従業員の給与・賞与計算が
//   「乙欄計算には OtsuWithholdingTaxCalculator が必要です」等で失敗する。
// 本ツールは移行元(現行DB)から乙税表一式をブラウザ版DBへコピーして修復する。既定は診断のみ、--fix で実行。

Console.OutputEncoding = System.Text.Encoding.UTF8;

// 乙欄計算が参照する表（給与＝乙税第1〜3表・階差、賞与＝賞与源泉徴収税額乙）。
string[] otsuTables =
{
    "T_乙税第１表", "T_乙税第２表", "T_乙税第３表", "T_乙税階差", "T_賞与源泉徴収税額乙",
};

bool doFix = args.Contains("--fix");
string dataDir = ArgValue("--data")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PayCalcDXPro", "data");
string legacy = ArgValue("--legacy")
    ?? @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\bin\DB\PayCalc2026.db";

Console.WriteLine($"DataDir = {dataDir}");
Console.WriteLine($"移行元  = {legacy}");
Console.WriteLine($"モード  = {(doFix ? "修復(--fix)" : "診断のみ")}\n");

using var store = new SecureDataStore(dataDir, DataProtectionScope.CurrentUser);
var dst = store.Connection;

Console.WriteLine("--- ブラウザ版DBの乙税表 現状 ---");
foreach (var t in otsuTables)
    Console.WriteLine($"  {t}: {Describe(dst, t)}");

if (args.Contains("--dump"))
{
    Console.WriteLine("\n--- 乙税表 全行ダンプ（シード作成用）---");
    foreach (var t in otsuTables) DumpRows(dst, t);
    return;
}

if (args.Contains("--drop"))
{
    // 恒久対策(EnsureSchemaの自動シード)の実証用：給与乙税表を削除して未移行状態を再現する。
    Console.WriteLine("\n--- 給与乙税表を削除（自動シード検証用）---");
    foreach (var t in new[] { "T_乙税第１表", "T_乙税第２表", "T_乙税第３表", "T_乙税階差" })
    {
        using var c = dst.CreateCommand();
        c.CommandText = $"DROP TABLE IF EXISTS \"{t}\"";
        c.ExecuteNonQuery();
        Console.WriteLine($"  drop {t}: {Describe(dst, t)}");
    }
    return;
}

Console.WriteLine("\n--- LoadOtsuTaxTables / LoadBonusOtsuRateTable 実行結果 ---");
var repo = new LegacyMasterRepository(dst);
DiagnoseLoad("給与乙欄(LoadOtsuTaxTables)", () => { var t = repo.LoadOtsuTaxTables(); new OtsuWithholdingTaxCalculator(t); });
DiagnoseLoad("賞与乙欄(LoadBonusOtsuRateTable)", () => repo.LoadBonusOtsuRateTable());

if (!doFix)
{
    Console.WriteLine("\n診断のみで終了しました。修復するには --fix を付けて再実行してください。");
    return;
}

// ---- 修復：移行元を復号し、乙税表一式をブラウザ版DBへコピー ----
Console.WriteLine("\n--- 修復開始（移行元を復号してコピー）---");
string tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_otsu_{Guid.NewGuid():N}.db");
LegacyDbDecryptor.DecryptFile(legacy, tmp);
try
{
    using var srcConn = new SqliteConnection($"Data Source={tmp}");
    srcConn.Open();
    foreach (var t in otsuTables)
        CopyTable(srcConn, dst, t);
}
finally
{
    SqliteConnection.ClearAllPools();
    if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } }
}

Console.WriteLine("\n--- 修復後の再診断 ---");
foreach (var t in otsuTables)
    Console.WriteLine($"  {t}: {Describe(dst, t)}");
DiagnoseLoad("給与乙欄(LoadOtsuTaxTables)", () =>
{
    var t = repo.LoadOtsuTaxTables();
    var calc = new OtsuWithholdingTaxCalculator(t);
    // サンプル計算（課税対象額 300,000・扶養0）で動作を確認。
    Console.WriteLine($"    サンプル: 課税対象額300,000 扶養0 → 乙税 {calc.Calculate(300_000m, 0):N0} 円");
});
DiagnoseLoad("賞与乙欄(LoadBonusOtsuRateTable)", () => repo.LoadBonusOtsuRateTable());
Console.WriteLine("\n✅ 修復完了。");

// ================= helpers =================
static string? ArgValue(string key)
{
    var a = Environment.GetCommandLineArgs();
    for (int i = 1; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
    return null;
}

static string Describe(SqliteConnection conn, string table)
{
    using var c = conn.CreateCommand();
    c.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n";
    c.Parameters.AddWithValue("$n", table);
    if (Convert.ToInt64(c.ExecuteScalar()) == 0) return "テーブル無し";
    using var c2 = conn.CreateCommand();
    c2.CommandText = $"SELECT count(*) FROM \"{table}\"";
    return $"存在・{Convert.ToInt64(c2.ExecuteScalar())} 行";
}

static void DumpRows(SqliteConnection conn, string table)
{
    using var check = conn.CreateCommand();
    check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n";
    check.Parameters.AddWithValue("$n", table);
    if (Convert.ToInt64(check.ExecuteScalar()) == 0) { Console.WriteLine($"[{table}] テーブル無し"); return; }
    Console.WriteLine($"[{table}]");
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"SELECT * FROM \"{table}\" ORDER BY No";
    using var r = cmd.ExecuteReader();
    var head = new List<string>();
    for (int i = 0; i < r.FieldCount; i++) head.Add(r.GetName(i));
    Console.WriteLine("  " + string.Join(" | ", head));
    while (r.Read())
    {
        var vals = new List<string>();
        for (int i = 0; i < r.FieldCount; i++) vals.Add(r.IsDBNull(i) ? "null" : r.GetValue(i).ToString() ?? "");
        Console.WriteLine("  " + string.Join(" | ", vals));
    }
}

static void DiagnoseLoad(string name, Action load)
{
    try { load(); Console.WriteLine($"  ✓ {name}: OK"); }
    catch (Exception ex) { Console.WriteLine($"  ✗ {name}: {ex.GetType().Name} {ex.Message}"); }
}

static void CopyTable(SqliteConnection src, SqliteConnection dst, string table)
{
    string? createSql;
    using (var c = src.CreateCommand())
    {
        c.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$n";
        c.Parameters.AddWithValue("$n", table);
        createSql = c.ExecuteScalar() as string;
    }
    if (createSql is null) { Console.WriteLine($"  ! {table}: 移行元に存在しないためスキップ"); return; }

    using (var c = dst.CreateCommand()) { c.CommandText = $"DROP TABLE IF EXISTS \"{table}\""; c.ExecuteNonQuery(); }
    using (var c = dst.CreateCommand()) { c.CommandText = createSql; c.ExecuteNonQuery(); }

    using var rc = src.CreateCommand();
    rc.CommandText = $"SELECT * FROM \"{table}\"";
    using var rd = rc.ExecuteReader();
    int cols = rd.FieldCount;
    var names = new List<string>();
    for (int i = 0; i < cols; i++) names.Add(rd.GetName(i));
    string colList = string.Join(",", names.Select(n => $"\"{n}\""));
    string parList = string.Join(",", Enumerable.Range(0, cols).Select(i => $"$p{i}"));

    int n = 0;
    using var tx = dst.BeginTransaction();
    while (rd.Read())
    {
        using var ic = dst.CreateCommand();
        ic.Transaction = tx;
        ic.CommandText = $"INSERT INTO \"{table}\" ({colList}) VALUES ({parList})";
        for (int i = 0; i < cols; i++)
            ic.Parameters.AddWithValue($"$p{i}", rd.IsDBNull(i) ? DBNull.Value : rd.GetValue(i));
        ic.ExecuteNonQuery();
        n++;
    }
    tx.Commit();
    Console.WriteLine($"  ✓ {table}: {n} 行コピー");
}
