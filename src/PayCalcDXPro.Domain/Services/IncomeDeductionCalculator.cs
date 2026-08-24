using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 年末調整の所得控除（基礎控除・配偶者控除・生命保険料控除）を計算する。
/// 現行 F_年調_従業員情報 の kisoKoujo / haigusyaKoujo / seihoKoujo に一致。
/// </summary>
public sealed class IncomeDeductionCalculator
{
    // 配偶者控除の本人合計所得しきい値（現行 MConst.KOJ_GAK_1/2/3）
    private const decimal 本人所得_900万 = 9_000_000m;
    private const decimal 本人所得_950万 = 9_500_000m;
    private const decimal 本人所得_1000万 = 10_000_000m;
    private const decimal 生保_区分上限 = 40_000m;    // SIH_MAX（新旧合算の区分上限）
    private const decimal 生保_合計上限 = 120_000m;   // SMI_HKN_MAX

    private readonly IncomeDeductionTables _t;

    public IncomeDeductionCalculator(IncomeDeductionTables tables) => _t = tables;

    /// <summary>基礎控除（本人の給与所得控除後調整後の金額で決定）。</summary>
    public decimal 基礎控除(decimal 本人合計所得)
    {
        BracketRow? row = _t.基礎控除.FirstOrDefault(r => 本人合計所得 >= r.下限 && 本人合計所得 < r.上限);
        return row?.加算額 ?? 0m;
    }

    /// <summary>配偶者（特別）控除。本人所得1000万超または配偶者なしは0。</summary>
    public decimal 配偶者控除(bool 配偶者あり, decimal 配偶者合計所得, bool 配偶者70歳以上, decimal 本人合計所得)
    {
        if (!配偶者あり) return 0m;
        if (本人合計所得 > 本人所得_1000万) return 0m;

        string ageRnk = 配偶者70歳以上 ? "2" : "1";
        SpouseDeductionRow? row = _t.配偶者控除.FirstOrDefault(r =>
            (r.年齢ランク == "0" || r.年齢ランク == ageRnk) &&
            配偶者合計所得 >= r.配偶者所得下限 && 配偶者合計所得 < r.配偶者所得上限);
        if (row is null) return 0m;

        if (本人合計所得 <= 本人所得_900万) return row.控除額1;
        if (本人合計所得 <= 本人所得_950万) return row.控除額2;
        return row.控除額3; // ≤1000万
    }

    /// <summary>生命保険料控除（一般＋介護医療＋個人年金、合計上限12万円）。</summary>
    public decimal 生命保険料控除(
        decimal 介護医療保険料,
        decimal 一般生命保険料_新, decimal 一般生命保険料_旧,
        decimal 個人年金保険料_新, decimal 個人年金保険料_旧)
    {
        decimal 介護 = 控除額_新(介護医療保険料);
        decimal 一般 = 新旧合算(控除額_新(一般生命保険料_新), 控除額_旧(一般生命保険料_旧));
        decimal 年金 = 新旧合算(控除額_新(個人年金保険料_新), 控除額_旧(個人年金保険料_旧));

        decimal 合計 = 一般 + 介護 + 年金;
        return Math.Min(合計, 生保_合計上限);
    }

    private decimal 控除額_新(decimal 保険料) => 控除額(保険料, _t.生保控除_新);
    private decimal 控除額_旧(decimal 保険料) => 控除額(保険料, _t.生保控除_旧);

    private static decimal 控除額(decimal 保険料, IReadOnlyList<BracketRow> table)
    {
        BracketRow row = table.First(r => 保険料 >= r.下限 && 保険料 < r.上限);
        return Math.Ceiling(保険料 * row.率 + row.加算額);
    }

    /// <summary>新旧合算：合算が区分上限(4万)超なら4万、ただし旧単独が大きければ旧を採用（現行準拠）。</summary>
    private static decimal 新旧合算(decimal 新, decimal 旧)
    {
        decimal total = 新 + 旧;
        if (total > 生保_区分上限) total = 生保_区分上限;
        if (total < 旧) total = 旧;
        return total;
    }
}
