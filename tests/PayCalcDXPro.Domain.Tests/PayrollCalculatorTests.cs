using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Reference;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>
/// B: ドメイン抽出PoC の検証。
/// 現行 WinForms から切り出した給与計算ロジックが、UI/DB なしで単体テストできることを示す。
/// （別表・料率は <see cref="SampleData"/>。本番では DB 値に差し替えて現行と突合する）
/// </summary>
public class PayrollCalculatorTests
{
    private static readonly PayrollCalculator Calc =
        new(SampleData.Rates, SampleData.TaxTables);

    [Fact]
    public void 代表ケース_標準報酬30万_扶養1人の計算結果が期待値どおり()
    {
        var input = new PayrollInput
        {
            課税支給合計 = 300_000m,
            非課税支給合計 = 10_000m,
            標準報酬月額 = 300_000m,
            扶養親族等の数 = 1,
            介護保険対象 = false,
        };

        PayrollResult r = Calc.Calculate(input);

        Assert.Equal(310_000m, r.総支給額);
        Assert.Equal(14_970m, r.社会保険.健康保険);
        Assert.Equal(0m, r.社会保険.介護保険);
        Assert.Equal(27_450m, r.社会保険.厚生年金);
        Assert.Equal(1_860m, r.社会保険.雇用保険);
        Assert.Equal(42_420m, r.社会保険.社会保険合計);
        Assert.Equal(5_140m, r.所得税); // (WK×5.105%)を10円未満四捨五入
        Assert.Equal(49_420m, r.控除合計);
        Assert.Equal(260_580m, r.差引支給額);
    }

    [Fact]
    public void 介護保険対象なら健保とは別に介護保険が控除される()
    {
        var baseInput = new PayrollInput { 課税支給合計 = 300_000m, 標準報酬月額 = 300_000m };

        PayrollResult 対象外 = Calc.Calculate(baseInput);
        PayrollResult 対象 = Calc.Calculate(baseInput with { 介護保険対象 = true });

        Assert.Equal(0m, 対象外.社会保険.介護保険);
        Assert.True(対象.社会保険.介護保険 > 0m);
        // 介護保険 = HalfWithSen(300000 * 0.0160 = 4800) = 2400
        Assert.Equal(2_400m, 対象.社会保険.介護保険);
    }

    [Fact]
    public void 低額給与は源泉所得税が0になる()
    {
        var input = new PayrollInput
        {
            課税支給合計 = 88_000m,
            標準報酬月額 = 88_000m,
            扶養親族等の数 = 0,
        };

        PayrollResult r = Calc.Calculate(input);

        Assert.Equal(0m, r.所得税);
    }

    [Fact]
    public void 扶養親族が増えるほど源泉所得税は減少する_単調性()
    {
        var input = new PayrollInput { 課税支給合計 = 400_000m, 標準報酬月額 = 400_000m };

        decimal 税0 = Calc.Calculate(input with { 扶養親族等の数 = 0 }).所得税;
        decimal 税1 = Calc.Calculate(input with { 扶養親族等の数 = 1 }).所得税;
        decimal 税2 = Calc.Calculate(input with { 扶養親族等の数 = 2 }).所得税;

        Assert.True(税0 > 税1);
        Assert.True(税1 > 税2);
    }

    [Theory]
    [InlineData(250_000, 0, false)]
    [InlineData(450_000, 2, true)]
    [InlineData(800_000, 1, true)]
    public void 差引支給額は常に_総支給マイナス控除合計に一致する_不変条件(
        decimal 課税支給, int 扶養, bool 介護)
    {
        var input = new PayrollInput
        {
            課税支給合計 = 課税支給,
            標準報酬月額 = 課税支給,
            扶養親族等の数 = 扶養,
            介護保険対象 = 介護,
            その他控除合計 = 12_000m,
        };

        PayrollResult r = Calc.Calculate(input);

        Assert.Equal(r.総支給額 - r.控除合計, r.差引支給額);
        Assert.Equal(r.社会保険.合計 + r.所得税 + r.その他控除合計, r.控除合計);
    }
}
