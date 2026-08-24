namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 賞与の社会保険料計算に用いる被保険者負担率（現行 T_料率 の被保険者負担分）。
/// 月次と異なり賞与は標準賞与額×率で算出するため、率（％）を直接保持する。
/// </summary>
public sealed record BonusInsuranceRates
{
    /// <summary>健康保険 被保険者負担率（％。例: 5.0）。</summary>
    public required decimal 健康保険率_本人 { get; init; }
    /// <summary>介護保険 被保険者負担率（％。例: 0.91）。</summary>
    public required decimal 介護保険率_本人 { get; init; }
    /// <summary>厚生年金 被保険者負担率（％。例: 9.15）。</summary>
    public required decimal 厚生年金率_本人 { get; init; }
    /// <summary>雇用保険 被保険者負担率（‰。例: 6 = 6/1000）。</summary>
    public required decimal 雇用保険率_本人 { get; init; }
}
