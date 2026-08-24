using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>
/// 所得金額調整控除の適用要件自動判定（現行 shotokuTyoseiKoujo/ckTyoSynzoku/chkFuyoSyunyu）の検証。
/// </summary>
public class IncomeAdjustmentEligibilityTests
{
    private const int 判定年 = 2026; // SYR_NEN
    private const decimal 超 = 8_500_001m;   // 850万円超
    private const decimal 以下 = 8_500_000m;  // 850万円ちょうど（対象外）

    // 年金所得換算スタブ：年金収入から30万円を控除した額を所得とみなす（テスト用）
    private static readonly Func<decimal, DateOnly, decimal> 年金換算 = (収入, _) => 収入 - 300_000m;
    private static readonly IncomeAdjustmentEligibilityEvaluator Eval = new(年金換算);

    private static DateOnly 歳(int 年齢) => new(判定年 - 年齢, 6, 1); // おおよそ当該年齢

    private static bool 判定(decimal 総額, bool 本人特障 = false, bool 配偶者申告 = false,
        調整控除配偶者? 配偶者 = null, params 調整控除扶養親族[] 扶養)
        => Eval.適用するか(総額, 本人特障, 配偶者申告, 配偶者, 扶養, 判定年);

    [Fact]
    public void 給与賞与総額が850万円以下なら常に対象外()
    {
        Assert.False(判定(以下, 本人特障: true));
    }

    [Fact]
    public void 本人が特別障害なら対象()
    {
        Assert.True(判定(超, 本人特障: true));
        Assert.False(判定(超)); // 他要件なし
    }

    [Fact]
    public void 配偶者扶養申告_ck_notがオンなら対象()
    {
        Assert.True(判定(超, 配偶者申告: true));
    }

    [Theory]
    [InlineData(収入種類.給与, 1_230_000, true, true)]   // 給与123万以下＋氏名あり＋特障→対象
    [InlineData(収入種類.給与, 1_230_001, true, false)]  // 給与123万超→対象外
    [InlineData(収入種類.所得, 580_000, true, true)]      // 所得58万以下→対象
    [InlineData(収入種類.所得, 580_001, true, false)]     // 所得58万超→対象外
    [InlineData(収入種類.年金, 1_230_000, true, true)]   // 配偶者の年金は収入で判定（123万以下）→対象
    [InlineData(収入種類.給与, 1_230_000, false, false)] // 氏名なし→対象外
    public void 配偶者が特別障害かつ収入要件と氏名入力を満たすとき対象(
        収入種類 種類, decimal 金額, bool 氏名あり, bool 期待)
    {
        var 配偶者 = new 調整控除配偶者 { 氏名あり = 氏名あり, 特別障害 = true, 収入種類 = 種類, 金額 = 金額 };
        Assert.Equal(期待, 判定(超, 配偶者: 配偶者));
    }

    [Fact]
    public void 配偶者が特別障害でなければ対象外()
    {
        var 配偶者 = new 調整控除配偶者 { 氏名あり = true, 特別障害 = false, 収入種類 = 収入種類.給与, 金額 = 0 };
        Assert.False(判定(超, 配偶者: 配偶者));
    }

    [Fact]
    public void 扶養が23歳未満かつ収入要件を満たし特定親族特別控除を受けていなければ対象()
    {
        var 扶養 = new 調整控除扶養親族
        {
            生年月日 = 歳(20), 収入種類 = 収入種類.給与, 金額 = 1_000_000m, 特定親族特別控除額 = 0m,
        };
        Assert.True(判定(超, 扶養: 扶養));
    }

    [Fact]
    public void 扶養が特定親族特別控除を受けている場合は調整控除の対象外()
    {
        var 扶養 = new 調整控除扶養親族
        {
            生年月日 = 歳(20), 収入種類 = 収入種類.給与, 金額 = 1_000_000m, 特定親族特別控除額 = 630_000m,
        };
        Assert.False(判定(超, 扶養: 扶養));
    }

    [Fact]
    public void 扶養が23歳以上でも特別障害なら対象()
    {
        var 扶養 = new 調整控除扶養親族
        {
            生年月日 = 歳(30), 特別障害 = true, 収入種類 = 収入種類.所得, 金額 = 500_000m,
        };
        Assert.True(判定(超, 扶養: 扶養));
    }

    [Fact]
    public void 扶養が23歳以上で特別障害でなければ対象外()
    {
        var 扶養 = new 調整控除扶養親族
        {
            生年月日 = 歳(30), 特別障害 = false, 収入種類 = 収入種類.所得, 金額 = 500_000m,
        };
        Assert.False(判定(超, 扶養: 扶養));
    }

    [Fact]
    public void 扶養の収入要件を満たさなければ対象外()
    {
        var 扶養 = new 調整控除扶養親族
        {
            生年月日 = 歳(20), 収入種類 = 収入種類.給与, 金額 = 1_500_000m, // 123万超
        };
        Assert.False(判定(超, 扶養: 扶養));
    }

    [Fact]
    public void 扶養の年金は所得換算後58万円以下なら対象()
    {
        // 年金収入80万 → 換算所得50万（≤58万）→ 対象
        var 対象 = new 調整控除扶養親族 { 生年月日 = 歳(20), 収入種類 = 収入種類.年金, 金額 = 800_000m };
        Assert.True(判定(超, 扶養: 対象));

        // 年金収入100万 → 換算所得70万（>58万）→ 対象外
        var 対象外 = new 調整控除扶養親族 { 生年月日 = 歳(20), 収入種類 = 収入種類.年金, 金額 = 1_000_000m };
        Assert.False(判定(超, 扶養: 対象外));
    }

    [Fact]
    public void 年金所得換算を注入しないと年金扶養判定で例外()
    {
        var eval = new IncomeAdjustmentEligibilityEvaluator(); // デリゲートなし
        var 扶養 = new 調整控除扶養親族 { 生年月日 = 歳(20), 収入種類 = 収入種類.年金, 金額 = 800_000m };
        Assert.Throws<InvalidOperationException>(
            () => eval.適用するか(超, false, false, null, new[] { 扶養 }, 判定年));
    }

    [Fact]
    public void 複数扶養のいずれかが該当すれば対象()
    {
        var 非該当 = new 調整控除扶養親族 { 生年月日 = 歳(30), 収入種類 = 収入種類.給与, 金額 = 1_000_000m };
        var 該当 = new 調整控除扶養親族 { 生年月日 = 歳(15), 収入種類 = 収入種類.給与, 金額 = 0m };
        Assert.True(判定(超, 扶養: new[] { 非該当, 該当 }));
    }
}
