using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Legacy;

// 現行（デスクトップ版）の暗号化SQLiteを、ブラウザ版が開く SQLCipher 暗号化DBへ移行し、
// 「現行保証」＝移行前後でデータ・マスタ（別表/料率/速算表 等）が完全一致することを検証する。
// 検証に1件でも不一致があれば終了コード1で失敗とする。
Console.OutputEncoding = Encoding.UTF8;

if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Windows専用です。"); return 1; }

string legacy = Arg("--legacy")
    ?? @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";
string dataDir = Arg("--out")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PayCalcDXPro", "data");
bool force = HasFlag("--force");
string legacyPassword = Arg("--password") ?? "SyuhouSystem";
// スタンドアロンPC（単一ユーザー）想定で既定は CurrentUser（管理者権限不要）。
var scope = (Arg("--scope") ?? "user").Equals("machine", StringComparison.OrdinalIgnoreCase)
    ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;

if (!File.Exists(legacy)) { Console.Error.WriteLine($"現行DBが見つかりません: {legacy}"); return 1; }

Console.WriteLine("=== 給与計算DXPro データ移行 ===");
Console.WriteLine($"現行DB : {legacy}");
Console.WriteLine($"移行先 : {dataDir}");
Console.WriteLine($"DPAPI  : {scope}");

if (SecureDataStore.Exists(dataDir))
{
    if (!force) { Console.Error.WriteLine("移行先に既存DBがあります。再実行は --force を付けてください。"); return 1; }
    File.Delete(Path.Combine(dataDir, "PayCalcDXPro.db"));
    Console.WriteLine("既存DBを削除して再移行します（--force）。");
}

// 1) 移行
SecureDataStore.MigrateLegacyInto(dataDir, legacy, scope, legacyPassword);
Console.WriteLine("→ 移行完了。現行保証の検証を行います...\n");

// 2) 検証用に両DBを開く（現行＝復号、移行先＝SQLCipher）
string legacyTmp = Path.Combine(Path.GetTempPath(), $"PayCalc_verify_{Guid.NewGuid():N}.db");
LegacyDbDecryptor.DecryptFile(legacy, legacyTmp, legacyPassword);

int failures = 0;
try
{
    using var legacyConn = new SqliteConnection($"Data Source={legacyTmp}");
    legacyConn.Open();
    using var store = new SecureDataStore(dataDir, scope);
    SqliteConnection migConn = store.Connection;

    // 2a) テーブル・行数の整合（全テーブル）
    Console.WriteLine("[1] テーブル・行数の整合");
    var tables = TableNames(legacyConn);
    int tableMismatch = 0;
    foreach (string t in tables)
    {
        long a = Count(legacyConn, t);
        long b = Count(migConn, t);
        if (a != b) { tableMismatch++; Console.WriteLine($"    ✗ {t}: 現行{a} → 移行{b}"); }
    }
    Console.WriteLine(tableMismatch == 0
        ? $"    ✓ 全 {tables.Count} テーブルの行数が一致"
        : $"    ✗ {tableMismatch} テーブルで不一致");
    failures += tableMismatch;

    // 2b) マスタ（計算の根拠）の完全一致：両DBから読み出して深く比較
    Console.WriteLine("[2] 計算マスタの一致（別表・料率・速算表 等）");
    var repoL = new LegacyMasterRepository(legacyConn);
    var repoM = new LegacyMasterRepository(migConn);
    var checks = new (string name, Func<LegacyMasterRepository, object> load)[]
    {
        ("源泉徴収税額表(別表一〜四)", r => r.LoadWithholdingTaxTables()),
        ("標準報酬月額表・雇用保険", r => r.LoadSocialInsuranceTable()),
        ("賞与源泉徴収税額(甲)", r => r.LoadBonusTaxRateTable()),
        ("賞与源泉徴収税額(乙)", r => r.LoadBonusOtsuRateTable()),
        ("料率(被保険者負担分)", r => r.LoadBonusInsuranceRates()),
        ("年調 年額表", r => r.LoadYearEndTables()),
        ("年調 所得控除表", r => r.LoadIncomeDeductionTables()),
        ("源泉所得税(乙欄)各表", r => r.LoadOtsuTaxTables()),
        ("扶養・障害者等控除表", r => r.LoadDependentDeductionTable()),
        ("公的年金等 速算表", r => r.LoadPensionConversionTable()),
    };
    foreach (var (name, load) in checks)
    {
        string ja = JsonSerializer.Serialize(load(repoL));
        string jb = JsonSerializer.Serialize(load(repoM));
        bool ok = ja == jb;
        Console.WriteLine($"    {(ok ? "✓" : "✗")} {name}");
        if (!ok) failures++;
    }
}
finally
{
    SqliteConnection.ClearAllPools();
    if (File.Exists(legacyTmp)) { try { File.Delete(legacyTmp); } catch { /* 無視 */ } }
}

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine("✅ 現行保証OK：移行データは現行と完全一致しました。");
    return 0;
}
Console.Error.WriteLine($"❌ 現行保証NG：{failures} 件の不一致があります。");
return 1;

// ---- helpers ----
static string? Arg(string key)
{
    string[] a = Environment.GetCommandLineArgs();
    for (int i = 1; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
    return null;
}
static bool HasFlag(string key) => Environment.GetCommandLineArgs().Contains(key);

static List<string> TableNames(SqliteConnection c)
{
    var list = new List<string>();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "select name from sqlite_master where type='table' and name not like 'sqlite_%' order by name";
    using var r = cmd.ExecuteReader();
    while (r.Read()) list.Add(r.GetString(0));
    return list;
}
static long Count(SqliteConnection c, string table)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = $"select count(*) from \"{table}\"";
    try { return (long)cmd.ExecuteScalar()!; } catch { return -1; }
}
