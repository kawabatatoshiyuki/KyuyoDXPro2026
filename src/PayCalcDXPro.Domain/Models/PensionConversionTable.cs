namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 公的年金等の速算表（現行 T_速算表）の1行。控除後ランク×年齢ランク×支給額区間で控除率・控除額を引く。
/// 区間は現行SQL（自 ≦ 年金 AND 至 ≧ 年金）に合わせ両端包含。
/// </summary>
public sealed record PensionConversionRow
{
    public required string 控除後ランク { get; init; }  // 現行 控除後ランク区分（"1"/"2"/"3"）
    public required string 年齢ランク { get; init; }    // 現行 年齢ランク（"1"=65歳未満 / "2"=65歳以上）
    public required decimal 支給額下限 { get; init; }    // 配偶者支給額_自（包含）
    public required decimal 支給額上限 { get; init; }    // 配偶者支給額_至（包含）
    public required decimal 控除率 { get; init; }        // %
    public required decimal 控除額 { get; init; }
}

/// <summary>公的年金等の速算表（現行 T_速算表）。</summary>
public sealed record PensionConversionTable
{
    public required IReadOnlyList<PensionConversionRow> 行 { get; init; }
}
