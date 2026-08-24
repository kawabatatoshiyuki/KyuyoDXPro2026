namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 賞与に対する源泉徴収税額の算出率の表（甲）。現行 T_賞与源泉徴収税額甲 相当。
/// 前月の社会保険料控除後の給与等の金額と扶養親族等の数から「賞与に乗ずる率(%)」を引き当てる。
/// </summary>
public sealed record BonusTaxRateTable
{
    /// <summary>率の昇順に並んだ区分行。</summary>
    public required IReadOnlyList<BonusRateRow> 行 { get; init; }

    /// <summary>前月給与（社保控除後）と扶養数から賞与税率(%)を求める。下限未満は0%。</summary>
    public decimal LookupRatePercent(decimal 前月社保控除後給与, int 扶養親族等の数)
    {
        int k = Math.Clamp(扶養親族等の数, 0, 7);
        decimal rate = 0m;
        foreach (var row in 行)
        {
            if (前月社保控除後給与 >= row.前月給与下限[k]) rate = row.賞与に乗ずる率;
            else break;
        }
        return rate;
    }
}

/// <summary>賞与算出率表の1行。前月給与下限[0..7]（扶養0人〜7人以上）と、その区分の率(%)。</summary>
public sealed record BonusRateRow
{
    /// <summary>扶養0人〜7人以上に対応する前月給与（社保控除後）の下限（長さ8）。</summary>
    public required decimal[] 前月給与下限 { get; init; }
    public decimal 賞与に乗ずる率 { get; init; }
}

/// <summary>
/// 賞与の算出率の表（乙）。現行 T_賞与源泉徴収税額乙。
/// 前月の社会保険料控除後の給与額から「賞与に乗ずる率(%)」を引き当てる（下限未満は先頭行の率）。
/// </summary>
public sealed record BonusOtsuRateTable
{
    /// <summary>金額（前月給与下限）の昇順に並んだ行。</summary>
    public required IReadOnlyList<BonusOtsuRow> 行 { get; init; }

    /// <summary>前月給与から賞与税率(%)を求める。下限未満は先頭行の率。</summary>
    public decimal LookupRatePercent(decimal 前月社保控除後給与)
    {
        decimal rate = 行.Count > 0 ? 行[0].賞与に乗ずる率 : 0m;
        foreach (var row in 行)
        {
            if (前月社保控除後給与 >= row.金額下限) rate = row.賞与に乗ずる率;
            else break;
        }
        return rate;
    }
}

/// <summary>乙の算出率表の1行（前月給与の下限と率%）。</summary>
public sealed record BonusOtsuRow
{
    public decimal 金額下限 { get; init; }
    public decimal 賞与に乗ずる率 { get; init; }
}
