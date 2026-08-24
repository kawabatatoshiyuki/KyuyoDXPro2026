using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>生年月日→年齢区分判定（現行 ProcessAgeDate/chkNenrei 準拠）の検証。DB不要。</summary>
public class AgeClassifierTests
{
    private const int 判定年 = 2026;

    [Theory]
    // 16歳の境界：(2026-16)=2010。01/01は16以上→一般、01/02は16未満→年少
    [InlineData("2010-01-01", 扶養年齢区分.一般)]
    [InlineData("2010-01-02", 扶養年齢区分.年少)]
    // 特定扶養（19-22）：19以上境界2007/01/01、23未満境界2003/01/02
    [InlineData("2007-01-01", 扶養年齢区分.特定扶養)]
    [InlineData("2005-06-15", 扶養年齢区分.特定扶養)]
    [InlineData("2003-01-02", 扶養年齢区分.特定扶養)]
    [InlineData("2003-01-01", 扶養年齢区分.一般)]   // 23歳以上→一般
    [InlineData("2007-01-02", 扶養年齢区分.一般)]   // 19歳未満（16-18）→一般
    // 老人（70+）：境界1956/01/01
    [InlineData("1956-01-01", 扶養年齢区分.老人)]
    [InlineData("1956-01-02", 扶養年齢区分.一般)]
    public void 生年月日から年齢区分を判定する(string birth, 扶養年齢区分 期待)
    {
        var d = DateOnly.Parse(birth);
        Assert.Equal(期待, AgeClassifier.扶養区分(d, 判定年));
    }

    [Theory]
    [InlineData("1956-01-01", true)]
    [InlineData("1956-01-02", false)]
    public void 配偶者老人控除の70歳以上判定(string birth, bool 期待)
    {
        Assert.Equal(期待, AgeClassifier.は70歳以上(DateOnly.Parse(birth), 判定年));
    }

    [Fact]
    public void 生年月日指定で特定扶養の控除額が630万になる()
    {
        var calc = new DependentDeductionCalculator(SampleDependentTable);
        // 2005年生まれ（判定年2026で21歳相当）→ 特定扶養 = 380,000 + 250,000
        decimal v = calc.扶養控除1人(DateOnly.Parse("2005-06-15"), 判定年);
        Assert.Equal(630_000m, v);
    }

    [Fact]
    public void 生年月日指定で年少扶養は0円_障害があれば加算のみ()
    {
        var calc = new DependentDeductionCalculator(SampleDependentTable);
        Assert.Equal(0m, calc.扶養控除1人(DateOnly.Parse("2015-05-05"), 判定年));
        Assert.Equal(750_000m, calc.扶養控除1人(DateOnly.Parse("2015-05-05"), 判定年, 同居: true, 障害: 障害区分.特別障害));
    }

    private static readonly DependentDeductionTable SampleDependentTable = new()
    {
        一般扶養 = 380_000m,
        特定扶養加算 = 250_000m,
        老人その他加算 = 100_000m,
        同居老親加算 = 200_000m,
        一般障害 = 270_000m,
        特別障害 = 400_000m,
        同居特別障害 = 750_000m,
        ひとり親 = 350_000m,
    };
}
