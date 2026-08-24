using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Data.YearEnd;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 年末調整の法定マスタ シード（<see cref="YearEndMasterRepository.EnsureSeeded"/>）が、
/// 計算が参照する全マスタ（年額表・所得控除表・扶養障害者等・特定親族特別控除・速算表）を
/// アプリDBへ正しく投入し、シードのみのDBで年税額計算が最後まで通ることを in-memory で検証する。
/// 現行マスタ未投入のDBで <c>no such table</c> となる回帰を防ぐ。
/// </summary>
public class YearEndMasterSeedTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public YearEndMasterSeedTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
    }

    [Fact]
    public void シード後に全マスタが読め年税額計算が通る()
    {
        new YearEndMasterRepository(_conn).EnsureSeeded();

        var repo = new LegacyMasterRepository(_conn);
        var yearTables = repo.LoadYearEndTables();            // T_給与所得控除後 / T_課税給与所得金額
        var dedTables = repo.LoadIncomeDeductionTables();     // T_基礎控除表 / T_配偶者控除 / T_生保控除１/２
        var depTable = repo.LoadDependentDeductionTable();    // T_扶養障害者等
        var srdTable = repo.LoadSpecialRelativeDeductionTable(); // T_特定親族特別控除
        var pensionTable = repo.LoadPensionConversionTable(); // T_速算表

        Assert.NotEmpty(yearTables.給与所得控除後);
        Assert.NotEmpty(dedTables.配偶者控除);
        Assert.NotEmpty(srdTable.行);
        Assert.NotEmpty(pensionTable.行);

        // 年間給与収入500万・社保70万のみの単純ケースで、計算が例外なく完了し正の年税額になること。
        var e = new YearEndEntry { 給与賞与計 = 5_000_000, 給与社保計 = 700_000, 源泉計 = 120_000 };
        YearEndCalculation.DeriveAutoDeductions(e, yearTables, depTable, srdTable, pensionTable, 2025);
        YearEndCalculation.Apply(e, yearTables, dedTables);

        Assert.True(e.基礎控除 > 0, "基礎控除が算定される");
        Assert.True(e.年調年税額 > 0, $"年調年税額が正（実際={e.年調年税額}）");
    }

    [Fact]
    public void EnsureSeeded_既存マスタがあれば再投入しない()
    {
        var repo = new YearEndMasterRepository(_conn);
        repo.EnsureSeeded();
        repo.EnsureSeeded();   // 2回目は何もしない（重複INSERTで主キー違反にならない）

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "select count(*) from T_給与所得控除後";
        Assert.Equal(6L, (long)cmd.ExecuteScalar()!);
    }

    public void Dispose() => _conn.Dispose();
}
