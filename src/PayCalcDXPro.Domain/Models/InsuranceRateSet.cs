namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 社会保険・労働保険の料率セット。現行の「料率」マスタ（F_料率）に対応。
/// 料率は都道府県・年度で変動するため、ハードコードせず必ず外部（DB）から注入する。
/// </summary>
public sealed record InsuranceRateSet
{
    /// <summary>健康保険料率（労使合計、例: 0.0998 = 9.98%）。</summary>
    public required decimal 健康保険料率 { get; init; }

    /// <summary>介護保険料率（労使合計、例: 0.0160 = 1.60%）。</summary>
    public required decimal 介護保険料率 { get; init; }

    /// <summary>厚生年金保険料率（労使合計、例: 0.183 = 18.3%）。</summary>
    public required decimal 厚生年金料率 { get; init; }

    /// <summary>雇用保険料率（被保険者負担分、例: 0.006 = 0.6%）。</summary>
    public required decimal 雇用保険料率_本人負担 { get; init; }
}
