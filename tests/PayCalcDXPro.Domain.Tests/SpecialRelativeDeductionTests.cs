using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>
/// 特定親族特別控除（現行 tokubetuKoujo）のドメインロジックを合成表で検証する。
/// 現行テンプレートDBに T_特定親族特別控除 が無いため、ここでは分岐・年齢要件・区間境界を
/// 合成データで厳密に確認する（実DB突合は SpecialRelativeDeductionFidelityTests でテーブル投入時に有効化）。
/// </summary>
public class SpecialRelativeDeductionTests
{
    private const int 判定年 = 2026; // SYR_NEN

    // 合成表：所得 (58万, 85万]→63万 / (85万, 123万]→11万、収入 (123万, 150万]→63万 / (150万, 188万]→11万
    private static readonly SpecialRelativeDeductionTable Table = new()
    {
        行 = new[]
        {
            new SpecialRelativeDeductionRow
            {
                合計所得下限 = 580_000m, 合計所得上限 = 850_000m,
                収入下限 = 1_230_000m, 収入上限 = 1_500_000m, 控除額 = 630_000m,
            },
            new SpecialRelativeDeductionRow
            {
                合計所得下限 = 850_000m, 合計所得上限 = 1_230_000m,
                収入下限 = 1_500_000m, 収入上限 = 1_880_000m, 控除額 = 110_000m,
            },
        },
    };

    private static readonly SpecialRelativeDeductionCalculator Calc = new(Table);

    private static 特定親族 親族(string 生年月日, 収入種類 種類, decimal 金額)
        => new() { 生年月日 = DateOnly.Parse(生年月日), 収入種類 = 種類, 金額 = 金額 };

    [Theory]
    [InlineData("2008-06-01", false)] // 18歳相当：若すぎる→対象外
    [InlineData("2005-06-01", true)]  // 20歳相当：対象
    [InlineData("2003-01-02", true)]  // 22歳相当の下限境界：対象
    [InlineData("2003-01-01", false)] // 23歳到達境界：対象外
    [InlineData("2006-12-31", true)]  // 19歳到達境界：対象
    public void 年齢19以上23未満のみ対象(string 生年月日, bool 対象)
    {
        decimal v = Calc.控除額1人(親族(生年月日, 収入種類.所得, 600_000m), 判定年);
        Assert.Equal(対象 ? 630_000m : 0m, v);
    }

    [Theory]
    [InlineData(580_000, 0)]       // 58万＝下限ちょうどは対象外（自は排他）
    [InlineData(580_001, 630_000)] // 58万超で1区分目
    [InlineData(850_000, 630_000)] // 85万＝1区分目の上限（至は包含）
    [InlineData(850_001, 110_000)] // 85万超で2区分目
    [InlineData(1_230_000, 110_000)] // 123万＝2区分目の上限
    [InlineData(1_230_001, 0)]     // 123万超は対象外
    public void 所得区分は下限排他_上限包含(decimal 所得, decimal 期待)
    {
        Assert.Equal(期待, Calc.控除額1人(親族("2005-06-01", 収入種類.所得, 所得), 判定年));
    }

    [Theory]
    [InlineData(1_230_000, 0)]       // 123万＝下限ちょうどは対象外
    [InlineData(1_500_000, 630_000)] // 150万＝1区分目の上限
    [InlineData(1_500_001, 110_000)] // 150万超で2区分目
    [InlineData(1_880_000, 110_000)] // 188万＝2区分目の上限
    [InlineData(1_880_001, 0)]       // 188万超は対象外
    public void 収入区分_給与は下限排他_上限包含(decimal 収入, decimal 期待)
    {
        Assert.Equal(期待, Calc.控除額1人(親族("2005-06-01", 収入種類.給与, 収入), 判定年));
    }

    [Fact]
    public void 年金は給与と同じ収入区分で判定される()
    {
        Assert.Equal(630_000m, Calc.控除額1人(親族("2005-06-01", 収入種類.年金, 1_400_000m), 判定年));
    }

    [Fact]
    public void その他の収入種類は常に0()
    {
        Assert.Equal(0m, Calc.控除額1人(親族("2005-06-01", 収入種類.その他, 600_000m), 判定年));
    }

    [Fact]
    public void 控除合計は対象者のみ加算する()
    {
        var 親族たち = new[]
        {
            親族("2005-06-01", 収入種類.所得, 600_000m), // 対象：63万
            親族("2005-06-01", 収入種類.給与, 1_600_000m), // 対象：11万
            親族("2008-06-01", 収入種類.所得, 600_000m), // 18歳：対象外
            親族("2005-06-01", 収入種類.所得, 2_000_000m), // 範囲外：0
        };
        Assert.Equal(740_000m, Calc.控除合計(親族たち, 判定年));
    }
}
