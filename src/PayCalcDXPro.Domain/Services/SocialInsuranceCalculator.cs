using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 社会保険料（健康保険・介護保険・厚生年金）と雇用保険料の「本人負担分」を料率から計算する。
/// 検証・概算用。現行の本番算出は表引き（<see cref="TableBasedSocialInsuranceCalculator"/>）が正。
/// </summary>
public sealed class SocialInsuranceCalculator : ISocialInsuranceCalculator
{
    private readonly InsuranceRateSet _r;

    public SocialInsuranceCalculator(InsuranceRateSet rates) => _r = rates;

    public SocialInsuranceResult Calculate(decimal 標準報酬月額, decimal 総支給額, bool 介護保険対象)
    {
        // 健保・介護・厚年は労使折半。本人負担は折半額の「50銭以下切捨て・50銭超切上げ」（協会けんぽ準拠）。
        decimal 健保 = HalfWithSenRounding(標準報酬月額 * _r.健康保険料率);
        decimal 介護 = 介護保険対象 ? HalfWithSenRounding(標準報酬月額 * _r.介護保険料率) : 0m;
        decimal 厚年 = HalfWithSenRounding(標準報酬月額 * _r.厚生年金料率);

        // 雇用保険は総支給額 × 本人負担料率。1円未満切捨て。
        decimal 雇用 = Math.Floor(総支給額 * _r.雇用保険料率_本人負担);

        return new SocialInsuranceResult
        {
            健康保険 = 健保,
            介護保険 = 介護,
            厚生年金 = 厚年,
            雇用保険 = 雇用,
        };
    }

    /// <summary>折半額の端数処理：50銭以下は切捨て、50銭超は切上げ。</summary>
    private static decimal HalfWithSenRounding(decimal total)
    {
        decimal half = total / 2m;
        decimal floor = Math.Floor(half);
        decimal frac = half - floor;
        return frac <= 0.5m ? floor : floor + 1m;
    }
}
