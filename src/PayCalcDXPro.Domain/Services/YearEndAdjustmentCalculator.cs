using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 年末調整の「年調年税額」を計算する（現行 年末調整処理の中核）。
/// 給与所得控除後（4,000円括り）→ 所得金額調整控除 → 課税給与所得金額 → 算出所得税額
/// → 年調所得税額（−住宅借入金等特別控除）→ 年調年税額（×102.1%）。
/// 現行 F_年調_従業員情報 のロジックに一致。
///
/// 所得控除合計を外から渡す <see cref="Calculate"/> と、基礎・配偶者・生命保険料控除を表から
/// 計算して完結させる <see cref="CalculateWithDeductions"/> の2系統を提供する。
/// </summary>
public sealed class YearEndAdjustmentCalculator
{
    // 現行 MConst と同値
    private const decimal 括り下限 = 1_900_000m;   // KYU_SYO_190
    private const decimal 括り上限 = 6_600_000m;   // KYU_SYO_660
    private const decimal 括り単位 = 4_000m;        // KYYSTK_BNBO
    private const decimal 調整控除判定収入 = 8_500_000m; // STK_KOJ_HNT_MAX
    private const decimal 調整控除上限 = 150_000m;       // STK_CSI_KOJ_MAX
    private const decimal 年税率 = 102.1m;               // NEN_ZEI_RTU（復興特別所得税込み）

    private readonly IncomeTaxYearTables _t;

    public YearEndAdjustmentCalculator(IncomeTaxYearTables tables) => _t = tables;

    /// <summary>所得控除額の合計を与えて年税額を計算する。</summary>
    public YearEndResult Calculate(
        decimal 年間給与収入,
        decimal 所得控除額の合計,
        bool 所得金額調整控除の適用 = false,
        decimal 住宅借入金等特別控除 = 0m)
    {
        (decimal 控除後, decimal 調整, decimal 調整後) = 給与所得控除後を計算(年間給与収入, 所得金額調整控除の適用);
        Tax t = 税額を計算(調整後, 所得控除額の合計, 住宅借入金等特別控除);

        return new YearEndResult
        {
            給与所得控除後の金額 = 控除後,
            所得金額調整控除 = 調整,
            給与所得控除後調整後 = 調整後,
            所得控除額の合計 = 所得控除額の合計,
            課税給与所得金額 = t.課税,
            算出所得税額 = t.算出,
            年調所得税額 = t.年調所得税額,
            年調年税額 = t.年税額,
        };
    }

    /// <summary>基礎・配偶者・生命保険料控除を表から計算し、年末調整を完結させる。</summary>
    public YearEndResult CalculateWithDeductions(YearEndDeductionInput input, IncomeDeductionCalculator deductions)
    {
        (decimal 控除後, decimal 調整, decimal 調整後) =
            給与所得控除後を計算(input.年間給与収入, input.所得金額調整控除の適用);

        // 基礎・配偶者控除は本人の合計所得（給与所得控除後調整後）で決定
        decimal 基礎 = deductions.基礎控除(調整後);
        decimal 配偶者 = deductions.配偶者控除(input.配偶者あり, input.配偶者合計所得, input.配偶者70歳以上, 調整後);
        decimal 生保 = deductions.生命保険料控除(
            input.介護医療保険料, input.一般生命保険料_新, input.一般生命保険料_旧,
            input.個人年金保険料_新, input.個人年金保険料_旧);

        decimal 所得控除合計 =
            input.社会保険料控除 + input.申告社会保険料 + input.小規模企業共済等掛金
            + 生保 + input.地震保険料控除 + 配偶者 + input.扶養控除等合計
            + input.特定親族特別控除合計 + 基礎;

        Tax t = 税額を計算(調整後, 所得控除合計, input.住宅借入金等特別控除);

        return new YearEndResult
        {
            給与所得控除後の金額 = 控除後,
            所得金額調整控除 = 調整,
            給与所得控除後調整後 = 調整後,
            基礎控除 = 基礎,
            配偶者控除 = 配偶者,
            生命保険料控除 = 生保,
            所得控除額の合計 = 所得控除合計,
            課税給与所得金額 = t.課税,
            算出所得税額 = t.算出,
            年調所得税額 = t.年調所得税額,
            年調年税額 = t.年税額,
        };
    }

    // ---- 内部計算 ----

    private (decimal 控除後, decimal 調整控除, decimal 調整後) 給与所得控除後を計算(decimal 年間給与収入, bool 調整適用)
    {
        // 4,000円括り（収入が[1,900,000, 6,600,000)のとき4,000円の倍数へ切捨て）
        decimal 年調給与額 = 年間給与収入;
        if (年間給与収入 >= 括り下限 && 年間給与収入 < 括り上限)
        {
            decimal 端数 = (年間給与収入 - 括り下限) % 括り単位;
            年調給与額 = 年間給与収入 - 端数;
        }

        // 給与所得控除後の給与等の金額 = Floor(年調給与額 × 率 + 加算額)
        BracketRow g = _t.給与所得控除後.First(r => 年調給与額 >= r.下限 && 年調給与額 < r.上限);
        decimal 控除後 = Math.Floor(年調給与額 * g.率 + g.加算額);
        if (控除後 < 0) 控除後 = 0m;

        // 所得金額調整控除 = Ceiling((収入 − 850万) / 10)、上限15万
        decimal 調整控除 = 0m;
        if (調整適用 && 年間給与収入 > 調整控除判定収入)
        {
            調整控除 = Math.Ceiling((年間給与収入 - 調整控除判定収入) / 10m);
            if (調整控除 > 調整控除上限) 調整控除 = 調整控除上限;
        }

        return (控除後, 調整控除, 控除後 - 調整控除);
    }

    private Tax 税額を計算(decimal 給与所得控除後調整後, decimal 所得控除額の合計, decimal 住宅借入金等特別控除)
    {
        // 課税給与所得金額（1,000円未満切捨て）
        decimal 課税 = 給与所得控除後調整後 - 所得控除額の合計;
        if (課税 < 0) 課税 = 0m;
        課税 = Math.Floor(課税 / 1000m) * 1000m;

        // 算出所得税額 = 課税 × 税率 − 控除額
        decimal 算出 = 0m;
        if (課税 > 0)
        {
            TaxRateRow row = _t.算出所得税速算.First(r => 課税 >= r.下限 && 課税 < r.上限);
            算出 = Math.Floor(課税 * row.税率 - row.控除額);
            if (算出 < 0) 算出 = 0m;
        }

        // 年調所得税額 = 算出所得税額 − 住宅借入金等特別控除（0未満は0）
        decimal 年調所得税額 = 算出 - 住宅借入金等特別控除;
        if (年調所得税額 < 0) 年調所得税額 = 0m;

        // 年調年税額 = 年調所得税額 × 102.1%、100円未満切捨て
        decimal 年税額 = Math.Floor(年調所得税額 * 年税率 / 100m / 100m) * 100m;

        return new Tax(課税, 算出, 年調所得税額, 年税額);
    }

    private readonly record struct Tax(decimal 課税, decimal 算出, decimal 年調所得税額, decimal 年税額);
}
