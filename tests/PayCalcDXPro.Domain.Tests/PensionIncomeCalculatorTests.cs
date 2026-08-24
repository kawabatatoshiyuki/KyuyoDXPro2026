using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>公的年金等の所得計算（現行 calNenkinSyotoku）の検証。</summary>
public class PensionIncomeCalculatorTests
{
    private const int 判定年 = 2026;
    private const decimal 大 = 99_999_999m;

    private static readonly PensionConversionTable Table = new()
    {
        行 = new[]
        {
            new PensionConversionRow { 控除後ランク = "1", 年齢ランク = "1", 支給額下限 = 0, 支給額上限 = 大, 控除率 = 10, 控除額 = 0 },
            new PensionConversionRow { 控除後ランク = "2", 年齢ランク = "1", 支給額下限 = 0, 支給額上限 = 大, 控除率 = 20, 控除額 = 0 },
            new PensionConversionRow { 控除後ランク = "3", 年齢ランク = "1", 支給額下限 = 0, 支給額上限 = 大, 控除率 = 30, 控除額 = 0 },
            new PensionConversionRow { 控除後ランク = "1", 年齢ランク = "2", 支給額下限 = 0, 支給額上限 = 大, 控除率 = 20, 控除額 = 0 },
        },
    };

    private static readonly PensionIncomeCalculator Calc = new(Table);

    private static DateOnly 若年 => new(2000, 6, 1); // 65歳未満

    [Theory]
    [InlineData(1_000_000, 100_000)]    // 1000万以下→ランク1（率10%）
    [InlineData(15_000_000, 3_000_000)] // 2000万以下→ランク2（率20%）
    [InlineData(25_000_000, 7_500_000)] // 2000万超→ランク3（率30%）
    public void 控除後ランクは年金収入で3段階(decimal 年金, decimal 期待)
    {
        Assert.Equal(期待, Calc.年金所得(年金, 若年, 判定年));
    }

    [Fact]
    public void 年齢ランクは65歳境界で切り替わる()
    {
        // 判定年2026：以上境界=1961/01/01。以前なら65歳以上（年齢ランク2＝率20%）。
        Assert.Equal(200_000m, Calc.年金所得(1_000_000m, new DateOnly(1961, 1, 1), 判定年)); // 65歳以上
        Assert.Equal(100_000m, Calc.年金所得(1_000_000m, new DateOnly(1961, 1, 2), 判定年)); // 65歳未満
    }

    [Fact]
    public void 所得は小数切り上げ()
    {
        // 1,000,001 × 10% = 100,000.1 → 切り上げ 100,001
        Assert.Equal(100_001m, Calc.年金所得(1_000_001m, 若年, 判定年));
    }

    [Fact]
    public void 該当行が無ければ0()
    {
        var empty = new PensionIncomeCalculator(new PensionConversionTable { 行 = Array.Empty<PensionConversionRow>() });
        Assert.Equal(0m, empty.年金所得(1_000_000m, 若年, 判定年));
    }

    [Fact]
    public void 換算関数は所得金額調整控除の年金扶養判定に注入できる()
    {
        var eval = new IncomeAdjustmentEligibilityEvaluator(Calc.換算関数(判定年));
        // 年金収入600万→所得 Ceiling(6,000,000×10%)=600,000 >58万 → 扶養に該当せず対象外
        var 扶養多 = new 調整控除扶養親族 { 生年月日 = 若年, 収入種類 = 収入種類.年金, 金額 = 6_000_000m };
        Assert.False(eval.適用するか(9_000_000m, false, false, null, new[] { 扶養多 }, 判定年));

        // 年金収入500万→所得 500,000 ≦58万 かつ23歳未満 → 対象
        var 扶養少 = new 調整控除扶養親族 { 生年月日 = new DateOnly(判定年 - 20, 6, 1), 収入種類 = 収入種類.年金, 金額 = 5_000_000m };
        Assert.True(eval.適用するか(9_000_000m, false, false, null, new[] { 扶養少 }, 判定年));
    }
}
