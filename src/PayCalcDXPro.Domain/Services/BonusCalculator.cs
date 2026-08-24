using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 賞与の源泉所得税を計算する（甲・乙）。現行 F_賞与処理 の税額計算に一致。
/// 賞与所得税 =（賞与の課税支給 − 賞与の社会保険料）×（賞与に乗ずる率）。
/// 率は前月の社会保険料控除後の給与額と扶養数から算出率の表で引き当てる。
/// 賞与の課税対象が前月給与（課税対象）の10倍を超える場合、または前月給与が無い場合は0。
/// </summary>
public sealed class BonusCalculator
{
    private readonly BonusTaxRateTable _kou;
    private readonly BonusOtsuRateTable? _otsu;

    public BonusCalculator(BonusTaxRateTable 甲表, BonusOtsuRateTable? 乙表 = null)
    {
        _kou = 甲表;
        _otsu = 乙表;
    }

    public BonusResult Calculate(
        decimal 賞与課税支給, decimal 賞与社会保険料,
        decimal 前月社保控除後給与, int 扶養親族等の数,
        源泉区分 源泉区分 = 源泉区分.甲, bool 前月給与あり = true)
    {
        decimal 課税対象 = 賞与課税支給 - 賞与社会保険料;
        if (課税対象 < 0) 課税対象 = 0m;

        // 前月給与が無い、または賞与課税が前月給与（課税対象）の10倍超 → 0（現行準拠）
        if (!前月給与あり || 課税対象 > 前月社保控除後給与 * 10m)
        {
            return new BonusResult { 課税対象額 = 課税対象, 賞与に乗ずる率 = 0m, 賞与所得税 = 0m };
        }

        decimal ratePct = 源泉区分 == 源泉区分.乙
            ? (_otsu ?? throw new InvalidOperationException("乙欄の賞与計算には乙の算出率表が必要です。"))
                .LookupRatePercent(前月社保控除後給与)
            : _kou.LookupRatePercent(前月社保控除後給与, 扶養親族等の数);

        decimal tax = Math.Floor(課税対象 * ratePct / 100m);

        return new BonusResult
        {
            課税対象額 = 課税対象,
            賞与に乗ずる率 = ratePct,
            賞与所得税 = tax,
        };
    }
}
