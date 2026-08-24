using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>賞与の社会保険料（標準賞与額×被保険者負担率・上限）と乙欄賞与税を現行DBで突合する。</summary>
public class BonusSocialInsuranceTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public BonusSocialInsuranceTests()
    {
        if (!File.Exists(LegacyDbPath)) return;
        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_bsi_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;
    private BonusSocialInsuranceCalculator SiCalc() => new(_repo!.LoadBonusInsuranceRates());

    [SkippableFact]
    public void 賞与社会保険料_標準賞与額に被保険者負担率を乗じる()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        // 総支給50万・介護なし：健保5%・厚年9.15%・雇用6/1000
        SocialInsuranceResult r = SiCalc().Calculate(500_000m, 介護保険対象: false);
        Assert.Equal(25_000m, r.健康保険);
        Assert.Equal(0m, r.介護保険);
        Assert.Equal(45_750m, r.厚生年金);
        Assert.Equal(3_000m, r.雇用保険);

        // 介護あり：0.91%
        Assert.Equal(4_550m, SiCalc().Calculate(500_000m, 介護保険対象: true).介護保険);
    }

    [SkippableFact]
    public void 賞与社会保険料_厚生年金は1回150万円が上限()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        SocialInsuranceResult r = SiCalc().Calculate(2_000_000m);
        Assert.Equal(137_250m, r.厚生年金);   // 1,500,000 × 9.15%
        Assert.Equal(100_000m, r.健康保険);   // 2,000,000 × 5%（健保は累計573万まで）
    }

    [SkippableFact]
    public void 賞与社会保険料_健保は年度累計573万円が上限()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        // 既支給550万＋今回50万＝600万＞573万。健保用標準賞与＝50万−(600万−573万)＝23万
        SocialInsuranceResult r = SiCalc().Calculate(500_000m, 既支給標準賞与額累計: 5_500_000m);
        Assert.Equal(11_500m, r.健康保険);    // 230,000 × 5%
        Assert.Equal(45_750m, r.厚生年金);    // 厚年は1回上限のみ（500,000 × 9.15%）
    }

    [SkippableFact]
    public void 乙欄賞与税_前月給与から率を引き当てる()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var calc = new BonusCalculator(_repo!.LoadBonusTaxRateTable(), _repo.LoadBonusOtsuRateTable());

        // 前月課税25万 → 乙表 222,000区分 → 20.42%
        BonusResult r1 = calc.Calculate(500_000m, 0m, 前月社保控除後給与: 250_000m, 扶養親族等の数: 0, 源泉区分: 源泉区分.乙);
        Assert.Equal(20.42m, r1.賞与に乗ずる率);
        Assert.Equal(102_100m, r1.賞与所得税);   // floor(500,000 × 20.42%)

        // 前月課税が先頭区分未満 → 先頭行の率10.21%
        BonusResult r2 = calc.Calculate(300_000m, 0m, 前月社保控除後給与: 100_000m, 扶養親族等の数: 0, 源泉区分: 源泉区分.乙);
        Assert.Equal(10.21m, r2.賞与に乗ずる率);
        Assert.Equal(30_630m, r2.賞与所得税);   // floor(300,000 × 10.21%)
    }

    [SkippableFact]
    public void 賞与税_前月給与の10倍超または前月給与なしは0()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        var calc = new BonusCalculator(_repo!.LoadBonusTaxRateTable(), _repo.LoadBonusOtsuRateTable());

        // 賞与課税50万 > 前月課税4万 × 10 = 40万 → 0
        Assert.Equal(0m, calc.Calculate(500_000m, 0m, 40_000m, 0).賞与所得税);
        // 前月給与なし → 0
        Assert.Equal(0m, calc.Calculate(500_000m, 0m, 300_000m, 0, 前月給与あり: false).賞与所得税);
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
