using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 公的年金等の収入から所得を計算する（現行 MFunction.calNenkinSyotoku に一致）。
/// 控除後ランク（年金収入で3段階）×年齢ランク（65歳以上/未満）×支給額区間で速算表を引き、
/// 所得 = Ceiling(年金収入 × 控除率/100 − 控除額)。該当行が無ければ0。
/// 所得金額調整控除の扶養要件判定（<see cref="IncomeAdjustmentEligibilityEvaluator"/>）の
/// 年金所得換算デリゲートとして注入できる。
/// </summary>
public sealed class PensionIncomeCalculator
{
    // 現行 MConst と同値（控除後ランクの境界）
    private const decimal ランク境界1 = 10_000_000m; // KOJ_RNK_GAK_1（1000万以下→ランク1）
    private const decimal ランク境界2 = 20_000_000m; // KOJ_RNK_GAK_2（2000万以下→ランク2、超→ランク3）

    private readonly PensionConversionTable _t;

    public PensionIncomeCalculator(PensionConversionTable table) => _t = table;

    /// <summary>年金収入→所得。</summary>
    public decimal 年金所得(decimal 年金収入, DateOnly 生年月日, int 判定年)
    {
        string ランク = 年金収入 <= ランク境界1 ? "1" : 年金収入 <= ランク境界2 ? "2" : "3";
        string 年齢 = AgeClassifier.は65歳以上(生年月日, 判定年) ? "2" : "1";

        PensionConversionRow? row = _t.行.FirstOrDefault(r =>
            r.控除後ランク == ランク && r.年齢ランク == 年齢 &&
            r.支給額下限 <= 年金収入 && r.支給額上限 >= 年金収入);
        if (row is null) return 0m;

        return Math.Ceiling(年金収入 * row.控除率 / 100m - row.控除額);
    }

    /// <summary>
    /// 所得金額調整控除の評価器へ渡す年金所得換算デリゲートを返す。
    /// </summary>
    public Func<decimal, DateOnly, decimal> 換算関数(int 判定年)
        => (年金収入, 生年月日) => 年金所得(年金収入, 生年月日, 判定年);
}
