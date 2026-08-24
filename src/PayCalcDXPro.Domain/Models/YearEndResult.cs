namespace PayCalcDXPro.Domain.Models;

/// <summary>年末調整の年税額計算結果。</summary>
public sealed record YearEndResult
{
    public required decimal 給与所得控除後の金額 { get; init; }
    /// <summary>所得金額調整控除額（給与収入850万円超で子育て等に該当する場合）。</summary>
    public decimal 所得金額調整控除 { get; init; }
    /// <summary>給与所得控除後（調整控除後）。課税所得算定の基礎。</summary>
    public required decimal 給与所得控除後調整後 { get; init; }

    // 所得控除の内訳（CalculateWithDeductions のとき設定。Calculate では既定値）
    public decimal 基礎控除 { get; init; }
    public decimal 配偶者控除 { get; init; }
    public decimal 生命保険料控除 { get; init; }
    public decimal 所得控除額の合計 { get; init; }

    public required decimal 課税給与所得金額 { get; init; }
    public required decimal 算出所得税額 { get; init; }
    /// <summary>年調所得税額（算出所得税額 − 住宅借入金等特別控除）。</summary>
    public required decimal 年調所得税額 { get; init; }
    /// <summary>年調年税額（復興特別所得税 2.1% 込み、100円未満切捨て）。</summary>
    public required decimal 年調年税額 { get; init; }
}
