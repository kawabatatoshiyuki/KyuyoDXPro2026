namespace PayCalcDXPro.Domain.Models;

/// <summary>月次給与計算の結果（明細書・台帳・帳票の元データ）。</summary>
public sealed record PayrollResult
{
    public required decimal 総支給額 { get; init; }
    public required SocialInsuranceResult 社会保険 { get; init; }
    public required decimal 所得税 { get; init; }
    public decimal その他控除合計 { get; init; }
    public required decimal 控除合計 { get; init; }
    public required decimal 差引支給額 { get; init; }
}
