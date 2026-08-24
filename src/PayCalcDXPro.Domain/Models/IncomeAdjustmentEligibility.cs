namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 所得金額調整控除（子ども・特別障害者等）の適用判定に使う配偶者情報。
/// 現行 shotokuTyoseiKoujo の配偶者分岐に対応。配偶者は年金も給与と同じ収入上限で判定する。
/// </summary>
public sealed record 調整控除配偶者
{
    /// <summary>配偶者氏名が入力済みか（現行 txtHaig 入力有無）。</summary>
    public bool 氏名あり { get; init; }
    public bool 特別障害 { get; init; }
    public 収入種類 収入種類 { get; init; }
    /// <summary>収入種類が「所得」なら合計所得金額、「給与/年金」なら収入金額。</summary>
    public decimal 金額 { get; init; }
}

/// <summary>
/// 所得金額調整控除の適用判定に使う扶養親族情報。現行 ckTyoSynzoku に対応。
/// </summary>
public sealed record 調整控除扶養親族
{
    public required DateOnly 生年月日 { get; init; }
    public bool 特別障害 { get; init; }
    public 収入種類 収入種類 { get; init; }
    /// <summary>収入種類が「所得」なら合計所得金額、「給与/年金」なら収入金額。</summary>
    public decimal 金額 { get; init; }
    /// <summary>当該親族の特定親族特別控除額（現行 shinzKojo）。0でなければ調整控除の対象から除く。</summary>
    public decimal 特定親族特別控除額 { get; init; }
}
