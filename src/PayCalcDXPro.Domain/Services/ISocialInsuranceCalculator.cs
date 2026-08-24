using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 社会保険・雇用保険（本人負担分）の算出方式を抽象化する。
/// 現行は「標準報酬月額表からの引き当て」（<see cref="TableBasedSocialInsuranceCalculator"/>）が正。
/// 料率からの計算（<see cref="SocialInsuranceCalculator"/>）は検証・概算用。
/// </summary>
public interface ISocialInsuranceCalculator
{
    /// <param name="報酬月額">保険料を引き当てる基礎額（表引き）または標準報酬月額（料率計算）。</param>
    /// <param name="総支給額">雇用保険の計算基礎。</param>
    /// <param name="介護保険対象">介護保険第2号被保険者か。</param>
    SocialInsuranceResult Calculate(decimal 報酬月額, decimal 総支給額, bool 介護保険対象);
}
