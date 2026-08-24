using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>扶養控除・障害者控除・寡婦/ひとり親/勤労学生控除を現行DB(T_扶養障害者等)で突合する。</summary>
public class DependentDeductionTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public DependentDeductionTests()
    {
        if (!File.Exists(LegacyDbPath)) return;
        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_dep_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;
    private DependentDeductionCalculator Calc() => new(_repo!.LoadDependentDeductionTable());

    [SkippableTheory]
    [InlineData(扶養年齢区分.一般, false, 障害区分.なし, 380_000)]
    [InlineData(扶養年齢区分.特定扶養, false, 障害区分.なし, 630_000)]   // 380,000 + 250,000
    [InlineData(扶養年齢区分.老人, true, 障害区分.なし, 580_000)]        // 同居老親 380,000 + 200,000
    [InlineData(扶養年齢区分.老人, false, 障害区分.なし, 480_000)]       // 老人その他 380,000 + 100,000
    [InlineData(扶養年齢区分.一般, false, 障害区分.一般障害, 650_000)]   // 380,000 + 270,000
    [InlineData(扶養年齢区分.一般, false, 障害区分.特別障害, 780_000)]   // 380,000 + 400,000
    [InlineData(扶養年齢区分.年少, true, 障害区分.特別障害, 750_000)]    // 年少は基礎なし、同居特別障害のみ
    [InlineData(扶養年齢区分.年少, false, 障害区分.なし, 0)]              // 年少（控除対象外）
    public void 扶養控除1人の額が現行表どおり(扶養年齢区分 区分, bool 同居, 障害区分 障害, int 期待)
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        decimal v = Calc().扶養控除1人(new 扶養親族 { 年齢区分 = 区分, 同居 = 同居, 障害 = 障害 });
        Assert.Equal(期待, v);
    }

    [SkippableFact]
    public void 障害者控除_配偶者は所得58万以下で適用()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        var c = Calc();
        Assert.Equal(270_000m, c.障害者控除_配偶者(障害区分.一般障害, false, 0m));
        Assert.Equal(750_000m, c.障害者控除_配偶者(障害区分.特別障害, true, 0m));   // 同居特別障害
        Assert.Equal(400_000m, c.障害者控除_配偶者(障害区分.特別障害, false, 0m));  // 非同居
        Assert.Equal(0m, c.障害者控除_配偶者(障害区分.一般障害, false, 600_000m));  // 所得超過
    }

    [SkippableFact]
    public void 本人控除_寡婦ひとり親障害勤労学生の所得要件()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        var c = Calc();
        Assert.Equal(350_000m, c.本人控除(障害区分.なし, 寡婦ひとり親区分.ひとり親, false, 3_000_000m));
        Assert.Equal(270_000m, c.本人控除(障害区分.なし, 寡婦ひとり親区分.寡婦, false, 3_000_000m));
        Assert.Equal(0m, c.本人控除(障害区分.なし, 寡婦ひとり親区分.ひとり親, false, 5_500_000m)); // 所得超過
        Assert.Equal(400_000m, c.本人控除(障害区分.特別障害, 寡婦ひとり親区分.なし, false, 9_000_000m));
        Assert.Equal(270_000m, c.本人控除(障害区分.なし, 寡婦ひとり親区分.なし, true, 800_000m));   // 勤労学生
        Assert.Equal(0m, c.本人控除(障害区分.なし, 寡婦ひとり親区分.なし, true, 900_000m));         // 勤労学生・所得超過
    }

    [SkippableFact]
    public void 年末調整_特定扶養1人を含めて控除明細から完結する()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var dep = Calc();
        decimal 人的控除 = dep.人的控除合計(
            扶養親族たち: new[] { new 扶養親族 { 年齢区分 = 扶養年齢区分.特定扶養 } },
            配偶者障害: 障害区分.なし, 配偶者同居: false, 配偶者合計所得: 0m,
            本人障害: 障害区分.なし, 寡婦ひとり親: 寡婦ひとり親区分.なし, 勤労学生: false, 本人合計所得: 3_560_000m);
        Assert.Equal(630_000m, 人的控除); // 特定扶養1人

        var yearEnd = new YearEndAdjustmentCalculator(_repo!.LoadYearEndTables());
        var ded = new IncomeDeductionCalculator(_repo.LoadIncomeDeductionTables());
        var input = new YearEndDeductionInput
        {
            年間給与収入 = 5_000_000m,
            社会保険料控除 = 750_000m,
            配偶者あり = true,
            配偶者合計所得 = 0m,
            介護医療保険料 = 50_000m,
            扶養控除等合計 = 人的控除,
        };

        YearEndResult r = yearEnd.CalculateWithDeductions(input, ded);

        // 所得控除合計 = 社保750,000 + 生保32,500 + 配偶者380,000 + 扶養630,000 + 基礎480,000
        Assert.Equal(2_272_500m, r.所得控除額の合計);
        Assert.Equal(1_287_000m, r.課税給与所得金額);
        Assert.Equal(64_350m, r.算出所得税額);
        Assert.Equal(65_700m, r.年調年税額);
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
