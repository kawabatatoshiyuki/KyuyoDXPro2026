using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 特定親族特別控除を現行DB(T_特定親族特別控除)で突合する。
/// 現行テンプレートDBには当該テーブルが未投入のため、テーブルが存在する環境でのみ自動的に有効化される
/// （存在しなければスキップ）。各行の「至」値（包含上限）で引いた控除額がその行の控除額に一致することを確認。
/// </summary>
public class SpecialRelativeDeductionFidelityTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private const int 判定年 = 2026; // SYR_NEN

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public SpecialRelativeDeductionFidelityTests()
    {
        if (!File.Exists(LegacyDbPath)) return;
        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_tss_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;

    private bool TableExists(string name)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "select count(*) from sqlite_master where type='table' and name=@n";
        cmd.Parameters.AddWithValue("@n", name);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    [SkippableFact]
    public void 現行テーブルがあれば各区分の控除額が一致する()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        Skip.IfNot(TableExists("T_特定親族特別控除"), "T_特定親族特別控除 が未投入のためスキップ");

        SpecialRelativeDeductionTable table = _repo!.LoadSpecialRelativeDeductionTable();
        Assert.NotEmpty(table.行);

        var calc = new SpecialRelativeDeductionCalculator(table);
        var 対象 = new DateOnly(判定年 - 20, 6, 1); // 20歳相当（年齢要件を満たす）

        foreach (SpecialRelativeDeductionRow row in table.行)
        {
            // 収入区分：123万円超〜188万円以下の行は「至」で引くとその行の控除額
            if (row.収入上限 > 1_230_000m && row.収入上限 <= 1_880_000m)
            {
                decimal v = calc.控除額1人(
                    new 特定親族 { 生年月日 = 対象, 収入種類 = 収入種類.給与, 金額 = row.収入上限 }, 判定年);
                Assert.Equal(row.控除額, v);
            }

            // 所得区分：58万円超〜123万円以下の行は「至」で引くとその行の控除額
            if (row.合計所得上限 > 580_000m && row.合計所得上限 <= 1_230_000m)
            {
                decimal v = calc.控除額1人(
                    new 特定親族 { 生年月日 = 対象, 収入種類 = 収入種類.所得, 金額 = row.合計所得上限 }, 判定年);
                Assert.Equal(row.控除額, v);
            }
        }
    }

    public void Dispose()
    {
        _conn?.Dispose();
        if (_tmp is not null && File.Exists(_tmp))
        {
            try { File.Delete(_tmp); } catch { /* 一時ファイル削除失敗は無視 */ }
        }
    }
}
