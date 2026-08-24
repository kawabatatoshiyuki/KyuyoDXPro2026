namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 源泉所得税「電算機計算の特例」で用いる別表（速算表）一式。
/// 現行システムでは別表第一〜第四を DB（月額表マスタ）に保持しており、
/// 本モデルへ読み出して計算器に注入する（= 税制改正時はデータ更新だけで対応可能）。
/// </summary>
public sealed record WithholdingTaxTables
{
    /// <summary>別表第一：給与所得控除の額（社会保険料控除後の金額に対する区分）。</summary>
    public required IReadOnlyList<BracketRow> 給与所得控除 { get; init; }

    /// <summary>別表第三：基礎控除の額（社会保険料控除後の金額に対する区分）。</summary>
    public required IReadOnlyList<BracketRow> 基礎控除 { get; init; }

    /// <summary>別表第四：税率速算表（課税給与所得金額に対する区分）。</summary>
    public required IReadOnlyList<TaxRateRow> 税率速算 { get; init; }

    /// <summary>別表第二：扶養親族等1人あたりの控除額（既定 31,667円 = 38万円 ÷ 12）。</summary>
    public decimal 扶養控除単価 { get; init; } = 31667m;
}

/// <summary>
/// 「下限以上・上限未満」の区分行（別表第一／第三）。
/// 控除額 = 対象金額 × 率 + 加算額。定額区分は 率 = 0 とする。
/// </summary>
public sealed record BracketRow
{
    public decimal 下限 { get; init; }
    public decimal 上限 { get; init; } = decimal.MaxValue;
    /// <summary>率（区分内で金額比例する場合。定額区分は 0）。</summary>
    public decimal 率 { get; init; }
    /// <summary>加算額（定額控除、または率に対する加算項）。</summary>
    public decimal 加算額 { get; init; }
}

/// <summary>別表第四：税額 = 課税給与所得金額 × 税率 − 控除額。</summary>
public sealed record TaxRateRow
{
    public decimal 下限 { get; init; }
    public decimal 上限 { get; init; } = decimal.MaxValue;
    public decimal 税率 { get; init; }
    public decimal 控除額 { get; init; }
}
