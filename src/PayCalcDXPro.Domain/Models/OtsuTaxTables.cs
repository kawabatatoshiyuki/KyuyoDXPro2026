namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 源泉所得税「乙欄」（従たる給与）の計算に用いる表。現行 T_乙税第1〜3表 / T_乙税階差 相当。
/// </summary>
public sealed record OtsuTaxTables
{
    /// <summary>乙税第1表＝給与所得控除（率/100＋加算額）。現行 T_乙税第１表。</summary>
    public required IReadOnlyList<BracketRow> 乙税第1表 { get; init; }

    /// <summary>乙税第2表＝基礎控除相当の定額（40,000円）。現行 T_乙税第２表。</summary>
    public required decimal 乙税第2表控除額 { get; init; }

    /// <summary>乙税第3表＝税率速算（税率/100−控除額、復興税抜き）。現行 T_乙税第３表。</summary>
    public required IReadOnlyList<TaxRateRow> 乙税第3表 { get; init; }

    /// <summary>乙税階差（10.5万〜74万の計算基準額を求める刻み）。現行 T_乙税階差。</summary>
    public required IReadOnlyList<乙税階差Row> 乙税階差 { get; init; }
}

/// <summary>乙税階差の1行（自以上・至未満の区分と刻み）。</summary>
public sealed record 乙税階差Row
{
    public decimal 給与額下限 { get; init; }
    public decimal 給与額上限 { get; init; } = decimal.MaxValue;
    public decimal 階差 { get; init; }
}
