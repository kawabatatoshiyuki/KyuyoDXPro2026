using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 標準報酬月額表（<see cref="SocialInsuranceTable"/>）を引き当てて社会保険料を決定する。
/// 現行（デスクトップ版）の算出方式に準拠。健保・介護・厚年は表の確定値、雇用保険のみ料率計算。
/// </summary>
public sealed class TableBasedSocialInsuranceCalculator : ISocialInsuranceCalculator
{
    private readonly SocialInsuranceTable _t;

    public TableBasedSocialInsuranceCalculator(SocialInsuranceTable table) => _t = table;

    public SocialInsuranceResult Calculate(decimal 報酬月額, decimal 総支給額, bool 介護保険対象)
    {
        SocialInsuranceBracket b = _t.区分.First(x => 報酬月額 >= x.報酬月額下限 && 報酬月額 < x.報酬月額上限);

        // 介護保険は表値に小数があるため、適用時に円未満切捨て（現行の処理に合わせる）。
        decimal 介護 = 介護保険対象 ? Math.Floor(b.介護保険) : 0m;

        decimal 雇用 = Math.Floor(総支給額 * _t.雇用保険料率_本人負担);

        return new SocialInsuranceResult
        {
            健康保険 = Math.Floor(b.健康保険),
            介護保険 = 介護,
            厚生年金 = Math.Floor(b.厚生年金),
            雇用保険 = 雇用,
        };
    }
}
