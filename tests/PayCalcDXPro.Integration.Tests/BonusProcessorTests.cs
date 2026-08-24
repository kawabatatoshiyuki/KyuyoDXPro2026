using PayCalcDXPro.Data.Bonus;
using PayCalcDXPro.Data.Payroll;
using PayCalcDXPro.Data.Rate;
using PayCalcDXPro.Domain.Models;
using Xunit;
using 源泉区分 = PayCalcDXPro.Data.Payroll.源泉区分;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// <see cref="BonusProcessor"/> の計算式を合成データ（手組みの料率・賞与算出率表）で網羅検証する。
/// 期待値は現行 F_賞与処理 のロジックから手計算した既知値で、DB の状態に依存しない。
/// </summary>
public class BonusProcessorTests
{
    // 50銭処理を必ず通すため小数2桁の率を持つ料率を用意する。
    private static RateSettings 料率() => new()
    {
        健康保険本人 = 5.13,
        介護保険本人 = 0.92,
        厚生年金本人 = 9.15,
        厚生年金基金本人 = 0.5,
        子育支援本人 = 0.36,
        雇用保険一般 = 6.0,
        雇用保険一般以外 = 7.0,
    };

    // 甲：前月給与（社保控除後）の下限と率。扶養数の列は全人数同値で簡略化する。
    private static BonusTaxRateTable 甲() => new()
    {
        行 = new List<BonusRateRow>
        {
            new() { 前月給与下限 = Eight(0),       賞与に乗ずる率 = 0m },
            new() { 前月給与下限 = Eight(100_000), 賞与に乗ずる率 = 4.084m },
            new() { 前月給与下限 = Eight(200_000), 賞与に乗ずる率 = 6.126m },
            new() { 前月給与下限 = Eight(400_000), 賞与に乗ずる率 = 8.168m },
        },
    };

    private static BonusOtsuRateTable 乙() => new()
    {
        行 = new List<BonusOtsuRow>
        {
            new() { 金額下限 = 0m,       賞与に乗ずる率 = 0m },
            new() { 金額下限 = 100_000m, 賞与に乗ずる率 = 3.063m },
            new() { 金額下限 = 250_000m, 賞与に乗ずる率 = 4.084m },
        },
    };

    private static decimal[] Eight(decimal v) => new[] { v, v, v, v, v, v, v, v };

    private static BonusProcessor Processor() => new(料率(), 甲(), 乙());

    private static BonusEmployeeSettings 全加入() => new()
    {
        健康保険加入 = true, 介護保険加入 = true, 子育支援加入 = true,
        厚生年金加入 = true, 年金基金加入 = true, 雇用保険加入 = true,
        労働保険適用 = "1", 源泉区分 = 源泉区分.甲, 源泉扶養親族数 = 0,
    };

    private static BonusEntry Entry(int 賞与) => new() { Number = "1", 回 = 1, 賞与 = 賞与 };

    [Fact]
    public void 社会保険_全項目の50銭チェーンと子育支援年金基金を算出する()
    {
        // 賞与499,500 → 標準賞与額（健保・年金とも）＝499,000。
        var e = Entry(499_500);
        Processor().Apply(e, 全加入(), new BonusContext());

        Assert.Equal(499_500, e.総支給額);
        // 健保＝round50(499000×5.13/100=25598.7)=25599
        Assert.Equal(25_599, e.健康保険);
        // 介護＝round50(健保25599＋round50(499000×0.92/100=4590.8→4591))−健保25599＝4591
        Assert.Equal(4_591, e.介護保険);
        // 厚年＝round50(499000×9.15/100=45658.5)→.5は切上げないので45658
        Assert.Equal(45_658, e.厚生年金);
        // 年金基金＝round50(499000×0.5/100=2495)=2495
        Assert.Equal(2_495, e.年金基金);
        // 子育支援＝round50(499000×0.36/100=1796.4)=1796
        Assert.Equal(1_796, e.子育支援);
        // 雇用＝round50(499500×6.0/1000=2997)=2997
        Assert.Equal(2_997, e.雇用保険);
        Assert.Equal(25_599 + 4_591 + 1_796 + 45_658 + 2_495 + 2_997, e.社会保険合計); // 83136
        Assert.Equal(499_500 - 83_136, e.課税対象額); // 416364（非課税・通勤費なし）
        // 前月給与未設定なので賞与所得税は0。
        Assert.Equal(0, e.所得税);
        // データ個数＝賞与/総支給/健保/介護/子育支援/厚年/年金基金/雇用/社保合計/課税対象/総控除/差引＝12
        Assert.Equal(12, e.データ個数);
    }

    [Fact]
    public void 雇用保険_労働保険一般以外の料率を使う()
    {
        var emp = 全加入();
        emp.労働保険適用 = "2"; // 7.0
        var e = Entry(499_500);
        Processor().Apply(e, emp, new BonusContext());
        // round50(499500×7.0/1000=3496.5)→端数0.5は「>0.5」でないため切捨て＝3496
        Assert.Equal(3_496, e.雇用保険);
    }

    [Fact]
    public void 子育支援未加入なら算出されず社保合計に乗らない()
    {
        var emp = 全加入();
        emp.子育支援加入 = false;
        var e = Entry(499_500);
        Processor().Apply(e, emp, new BonusContext());
        Assert.Null(e.子育支援);
        Assert.Equal(25_599 + 4_591 + 45_658 + 2_495 + 2_997, e.社会保険合計); // 子育支援1796を除く
    }

    [Fact]
    public void 複数回支給で健保介護は年度上限573万を通算して頭打ちになる()
    {
        // 既に550万支給済み＋当回50万＝600万 → 上限573万まで＝当回分の標準賞与は23万に圧縮。
        var emp = new BonusEmployeeSettings { 健康保険加入 = true, 労働保険適用 = "1" };
        var e = Entry(500_000);
        var ctx = new BonusContext { 既支給賞与総支給 = new[] { 5_500_000 } };
        Processor().Apply(e, emp, ctx);

        // 健保基準額＝floor1000(500000−(6000000−5730000))=floor1000(230000)=230000
        // 健保＝round50(230000×5.13/100=11799)=11799
        Assert.Equal(11_799, e.健康保険);
    }

    [Fact]
    public void 賞与所得税_甲_前月給与の課税対象から率を引く()
    {
        // 社保を持たせず課税対象＝総支給で税額だけを検証する。
        var emp = new BonusEmployeeSettings { 源泉区分 = 源泉区分.甲, 源泉扶養親族数 = 0 };
        var e = Entry(300_000);
        var ctx = new BonusContext { 前月給与課税対象額 = 200_000 };
        Processor().Apply(e, emp, ctx);

        Assert.Equal(300_000, e.課税対象額);
        // 前月200,000・扶養0 → 率6.126%。税＝floor(300000×6.126/100=18378)=18378
        Assert.Equal(18_378, e.所得税);
    }

    [Fact]
    public void 賞与所得税_乙_乙の算出率表から率を引く()
    {
        var emp = new BonusEmployeeSettings { 源泉区分 = 源泉区分.乙 };
        var e = Entry(300_000);
        var ctx = new BonusContext { 前月給与課税対象額 = 200_000 };
        Processor().Apply(e, emp, ctx);

        // 前月200,000 → 乙率3.063%。税＝floor(300000×3.063/100=9189)=9189
        Assert.Equal(9_189, e.所得税);
    }

    [Fact]
    public void 賞与所得税_前月給与の10倍を超える賞与は0()
    {
        var emp = new BonusEmployeeSettings { 源泉区分 = 源泉区分.甲 };
        var e = Entry(300_000);
        var ctx = new BonusContext { 前月給与課税対象額 = 20_000 }; // ×10=200000 < 300000
        Processor().Apply(e, emp, ctx);
        Assert.Equal(0, e.所得税);
    }

    [Fact]
    public void 賞与所得税_前月給与が無い場合は0()
    {
        var emp = new BonusEmployeeSettings { 源泉区分 = 源泉区分.甲 };
        var e = Entry(300_000);
        Processor().Apply(e, emp, new BonusContext { 前月給与課税対象額 = null });
        Assert.Equal(0, e.所得税);
    }

    [Fact]
    public void 賞与所得税_計算しない区分は0()
    {
        var emp = new BonusEmployeeSettings { 源泉区分 = 源泉区分.計算しない };
        var e = Entry(300_000);
        var ctx = new BonusContext { 前月給与課税対象額 = 200_000 };
        Processor().Apply(e, emp, ctx);
        Assert.Equal(0, e.所得税);
    }

    [Fact]
    public void 総控除と差引_控除項目9まで含め還付徴収を反映する()
    {
        var emp = new BonusEmployeeSettings(); // 全未加入＝社保0
        var e = Entry(300_000);
        e.控除項目[0] = 3_000; // 控除1
        e.控除項目[8] = 1_000; // 控除9（賞与は総控除に含める）
        e.還付金額 = 500;
        e.徴収金額 = 200;
        Processor().Apply(e, emp, new BonusContext());

        Assert.Equal(0, e.社会保険合計);
        // 総控除＝社保0＋所得税0＋控除1(3000)＋控除9(1000)＝4000
        Assert.Equal(4_000, e.総控除額);
        // 差引＝300000−4000＋500−200＝296300
        Assert.Equal(296_300, e.差引支給額);
    }

    [Fact]
    public void 支給項目16と賞与本体を総支給に合算する()
    {
        var emp = new BonusEmployeeSettings();
        var e = Entry(200_000);
        e.賞与支給[0] = 10_000;  // 手当項目1
        e.賞与支給[11] = 5_000;  // 手当項目12
        e.賞与支給[15] = 3_000;  // 任意項目4
        Processor().Apply(e, emp, new BonusContext());
        Assert.Equal(200_000 + 10_000 + 5_000 + 3_000, e.総支給額);
    }
}
