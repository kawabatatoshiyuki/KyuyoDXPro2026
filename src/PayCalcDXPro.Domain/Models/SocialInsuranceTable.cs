namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 標準報酬月額表（現行 T_料率保険料額表 相当）。
/// 報酬月額の区分ごとに、健康保険・介護保険・厚生年金の「被保険者負担分」を確定値で保持する。
/// 現行システムはこの表を引き当てて保険料を決定する（料率からの都度計算ではない）。
/// </summary>
public sealed record SocialInsuranceTable
{
    public required IReadOnlyList<SocialInsuranceBracket> 区分 { get; init; }

    /// <summary>雇用保険料率（被保険者負担分。例: 0.006 = 6/1000）。</summary>
    public required decimal 雇用保険料率_本人負担 { get; init; }
}

/// <summary>報酬月額の区分（自以上・至未満）と、その区分の保険料（被保険者負担分）。</summary>
public sealed record SocialInsuranceBracket
{
    public decimal 報酬月額下限 { get; init; }
    public decimal 報酬月額上限 { get; init; } = decimal.MaxValue;
    public decimal 健康保険 { get; init; }
    public decimal 介護保険 { get; init; }
    public decimal 厚生年金 { get; init; }
}
