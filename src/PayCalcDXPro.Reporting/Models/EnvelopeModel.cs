namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 給与/賞与封筒印刷（現行 R_封筒／R_封筒Y）。選択した従業員ごとに長3封筒1名1枚を出力する。
/// </summary>
public sealed record EnvelopeModel
{
    public string 会社名 { get; init; } = "";
    /// <summary>「給与明細書」または「賞与明細書」。</summary>
    public string 明細書名 { get; init; } = "";
    /// <summary>給与＝「2026年6月分」／賞与＝「2026年6月 賞与」。</summary>
    public string 支給日 { get; init; } = "";
    /// <summary>true＝横長封筒（R_封筒Y）／false＝縦長封筒（R_封筒）。</summary>
    public bool 横 { get; init; }
    public IReadOnlyList<EnvelopeRow> 明細 { get; init; } = Array.Empty<EnvelopeRow>();
}

public sealed record EnvelopeRow(
    string No,
    string 部門,
    string 氏名);
