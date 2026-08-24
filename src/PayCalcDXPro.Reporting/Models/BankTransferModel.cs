namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 銀行振込一覧表（現行 R_銀行表）。振込（現金支給以外）の従業員ごとに振込先・口座・金額を一覧する。
/// </summary>
public sealed record BankTransferModel
{
    public string 会社名 { get; init; } = "";
    /// <summary>給与＝「2025年12月分」／賞与＝「2026年7月賞与」。</summary>
    public string 支給見出し { get; init; } = "";
    /// <summary>支給日（例「1月5日支払」）。</summary>
    public string 支給日 { get; init; } = "";
    public IReadOnlyList<BankTransferRow> 明細 { get; init; } = Array.Empty<BankTransferRow>();
    public int 合計件数 { get; init; }
    public long 合計金額 { get; init; }
}

public sealed record BankTransferRow(
    string 銀行名,
    string 支店名,
    string 種類,
    string 口座番号,
    string No,
    string 口座名義人,
    string カナ,
    long 振込金額);
