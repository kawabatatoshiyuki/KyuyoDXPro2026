namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 年末調整で用いる年額の表。現行 T_給与所得控除後 / T_課税給与所得金額 相当。
/// </summary>
public sealed record IncomeTaxYearTables
{
    /// <summary>給与所得控除後の給与等の金額（年収に対する 率(%)＋加算額）。現行 T_給与所得控除後。</summary>
    public required IReadOnlyList<BracketRow> 給与所得控除後 { get; init; }

    /// <summary>算出所得税額の速算表（課税給与所得金額に対する 税率(%)−控除額）。現行 T_課税給与所得金額。</summary>
    public required IReadOnlyList<TaxRateRow> 算出所得税速算 { get; init; }
}
