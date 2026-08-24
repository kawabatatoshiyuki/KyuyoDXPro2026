using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using System.Text;

// 現行DBを復号し、指定テーブルの CREATE 文（スキーマ）を表示する。
Console.OutputEncoding = Encoding.UTF8;

string src = args.Length > 0 ? args[0]
    : @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\bin\DB\PayCalc2026.db";
string table = args.Length > 1 ? args[1] : "T_マイナンバー情報";

string tmp = Path.Combine(Path.GetTempPath(), "PayCalc_schema.db");
LegacyDbDecryptor.DecryptFile(src, tmp);

using var conn = new SqliteConnection($"Data Source={tmp}");
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$n";
cmd.Parameters.AddWithValue("$n", table);
var sql = cmd.ExecuteScalar() as string;
Console.WriteLine(sql ?? $"{table} が見つかりません");

using var c2 = conn.CreateCommand();
c2.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
try { Console.WriteLine($"\n行数: {c2.ExecuteScalar()}"); } catch { }
