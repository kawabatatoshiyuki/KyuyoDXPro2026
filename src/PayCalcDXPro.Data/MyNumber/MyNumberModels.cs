namespace PayCalcDXPro.Data.MyNumber;

/// <summary>
/// マイナンバー情報（現行 T_マイナンバー情報：本人・配偶者・扶養親族1〜7）。
/// アプリ内では平文を扱い、保存時にのみ列単位で暗号化する。
/// </summary>
public sealed record MyNumberRecord
{
    public required string Number { get; init; }     // 従業員番号（主キー）
    public string? 本人 { get; init; }
    public string? 配偶者 { get; init; }
    /// <summary>扶養親族1〜7（長さ7）。</summary>
    public IReadOnlyList<string?> 扶養親族 { get; init; } = new string?[7];
}
