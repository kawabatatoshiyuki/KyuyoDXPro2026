using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 賞与の社会保険料（本人負担分）を計算する。現行 F_賞与処理 の社会保険計算に一致。
/// 標準賞与額（総支給の千円未満切捨て）× 被保険者負担率。50銭超切上げ・50銭以下切捨て。
/// 健康保険・介護は年度累計573万円、厚生年金は1回150万円の上限を適用。
/// </summary>
public sealed class BonusSocialInsuranceCalculator
{
    private const decimal 健保介護_年度上限 = 5_730_000m; // SYOYO_SYAKAIHOKEN_JOUGENGAKU
    private const decimal 厚年_1回上限 = 1_500_000m;       // SYOYO_NENKIN_JOUGENGAKU

    private readonly BonusInsuranceRates _r;

    public BonusSocialInsuranceCalculator(BonusInsuranceRates rates) => _r = rates;

    /// <param name="賞与総支給額">賞与の総支給額。</param>
    /// <param name="既支給標準賞与額累計">同一年度に既に支給済みの標準賞与額の累計（健保上限判定用）。</param>
    /// <param name="介護保険対象">介護保険第2号被保険者か。</param>
    /// <param name="雇用保険対象">雇用保険の被保険者か。</param>
    public SocialInsuranceResult Calculate(
        decimal 賞与総支給額,
        decimal 既支給標準賞与額累計 = 0m,
        bool 介護保険対象 = false,
        bool 雇用保険対象 = true)
    {
        decimal 標準賞与額 = Floor千円(賞与総支給額);

        // 健康保険・介護：年度累計573万円が上限
        decimal 累計 = 標準賞与額 + 既支給標準賞与額累計;
        decimal 健保用標準賞与 = 標準賞与額;
        if (累計 > 健保介護_年度上限)
        {
            健保用標準賞与 = Floor千円(賞与総支給額 - (累計 - 健保介護_年度上限));
            if (健保用標準賞与 < 0) 健保用標準賞与 = 0m;
        }

        // 厚生年金：1回あたり150万円が上限
        decimal 厚年用標準賞与 = 賞与総支給額 > 厚年_1回上限 ? 厚年_1回上限 : 標準賞与額;

        decimal 健保 = Round50(健保用標準賞与 * _r.健康保険率_本人 / 100m);
        decimal 介護 = 介護保険対象 ? Round50(健保用標準賞与 * _r.介護保険率_本人 / 100m) : 0m;
        decimal 厚年 = Round50(厚年用標準賞与 * _r.厚生年金率_本人 / 100m);
        decimal 雇用 = 雇用保険対象 ? Round50(賞与総支給額 * _r.雇用保険率_本人 / 1000m) : 0m;

        return new SocialInsuranceResult
        {
            健康保険 = 健保,
            介護保険 = 介護,
            厚生年金 = 厚年,
            雇用保険 = 雇用,
        };
    }

    private static decimal Floor千円(decimal v) => Math.Floor(v / 1000m) * 1000m;

    /// <summary>50銭超は切上げ、50銭以下は切捨て（現行賞与の端数処理）。</summary>
    private static decimal Round50(decimal x)
    {
        decimal floor = Math.Floor(x);
        return (x - floor) > 0.5m ? floor + 1m : floor;
    }
}
