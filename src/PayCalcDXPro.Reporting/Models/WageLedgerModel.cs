namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 賃金台帳（支給日別）。現行 R_給台帳月／R_賞台帳月 準拠。
/// 「項目（行）×従業員（列）」のクロス集計で、ページごとの小計と全体合計を持つ。
/// </summary>
public sealed record WageLedgerModel
{
    public string 会社名 { get; init; } = "";
    /// <summary>部門名。全部門なら空。</summary>
    public string 部門 { get; init; } = "";
    /// <summary>右上の年表示（例「2026年」）。</summary>
    public string 年 { get; init; } = "";
    /// <summary>true=賞与台帳／false=給与台帳。</summary>
    public bool Is賞与 { get; init; }
    /// <summary>給与＝「2025年12月分」／賞与＝「第1回目賞与」。</summary>
    public string 支給見出し { get; init; } = "";
    /// <summary>支給日（例「12月5日支払」）。</summary>
    public string 支給日 { get; init; } = "";
    /// <summary>締日（給与のみ。例「前月末日締め」）。</summary>
    public string 締日 { get; init; } = "";

    /// <summary>従業員列（No・氏名）。並び順。</summary>
    public IReadOnlyList<LedgerEmployee> 従業員 { get; init; } = Array.Empty<LedgerEmployee>();
    /// <summary>項目行。</summary>
    public IReadOnlyList<LedgerRow> 行 { get; init; } = Array.Empty<LedgerRow>();
}

public sealed record LedgerEmployee(string No, string 氏名);

/// <summary>
/// 台帳の1項目行。値は従業員ごと（従業員リストと同順）。
/// 金額=true は円（整数・3桁区切り）、false は勤怠（小数2桁）。
/// 太線=true でグループ区切りの上罫を太くする。
/// </summary>
public sealed record LedgerRow(
    string ラベル,
    IReadOnlyList<double?> 値,
    bool 金額 = true,
    bool 太線 = false);
