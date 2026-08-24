namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 特定親族（19歳以上23歳未満）の収入種類。現行 cmb_年間_親族 の選択値
/// （MConst.CMB_SZK_NKN_2=給与 / _3=年金 / _4=所得）に対応する。
/// </summary>
public enum 収入種類
{
    その他 = 0,
    給与 = 2,
    年金 = 3,
    所得 = 4,
}

/// <summary>
/// 特定親族特別控除表（現行 T_特定親族特別控除）の1行。
/// 同じ控除額を「合計所得金額」と「収入金額」の両方の区間で引けるよう、1行に両方の上下限を持つ。
/// 区間は現行SQL（自 &lt; 金額 AND 至 ≧ 金額）に合わせ「下限は排他・上限は包含」（下限, 上限]。
/// </summary>
public sealed record SpecialRelativeDeductionRow
{
    public required decimal 合計所得下限 { get; init; }  // 合計所得金額_自（排他）
    public required decimal 合計所得上限 { get; init; }  // 合計所得金額_至（包含）
    public required decimal 収入下限 { get; init; }      // 収入金額_自（排他）
    public required decimal 収入上限 { get; init; }      // 収入金額_至（包含）
    public required decimal 控除額 { get; init; }        // 特定親族特別控除額
}

/// <summary>特定親族特別控除の控除額表（現行 T_特定親族特別控除）。</summary>
public sealed record SpecialRelativeDeductionTable
{
    public required IReadOnlyList<SpecialRelativeDeductionRow> 行 { get; init; }
}

/// <summary>特定親族1人分の入力（生年月日・収入種類・金額）。</summary>
public sealed record 特定親族
{
    public required DateOnly 生年月日 { get; init; }
    public required 収入種類 収入種類 { get; init; }
    /// <summary>収入種類が「所得」なら合計所得金額、「給与/年金」なら収入金額。</summary>
    public required decimal 金額 { get; init; }
}
