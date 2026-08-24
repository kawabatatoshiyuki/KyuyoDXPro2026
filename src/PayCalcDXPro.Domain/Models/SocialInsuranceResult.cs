namespace PayCalcDXPro.Domain.Models;

/// <summary>社会保険・雇用保険の本人負担分の内訳。</summary>
public sealed record SocialInsuranceResult
{
    public decimal 健康保険 { get; init; }
    public decimal 介護保険 { get; init; }
    public decimal 厚生年金 { get; init; }
    public decimal 雇用保険 { get; init; }

    /// <summary>現行の「社会保険合計」項目に対応（健保＋介護＋厚年）。</summary>
    public decimal 社会保険合計 => 健康保険 + 介護保険 + 厚生年金;

    /// <summary>源泉所得税の課税対象算定に用いる社会保険料等の総額（雇用保険を含む）。</summary>
    public decimal 合計 => 健康保険 + 介護保険 + 厚生年金 + 雇用保険;
}
