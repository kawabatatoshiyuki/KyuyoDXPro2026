namespace PayCalcDXPro.Domain.Models;

/// <summary>賞与計算の結果。</summary>
public sealed record BonusResult
{
    public required decimal 課税対象額 { get; init; }
    public required decimal 賞与に乗ずる率 { get; init; }
    public required decimal 賞与所得税 { get; init; }
}
