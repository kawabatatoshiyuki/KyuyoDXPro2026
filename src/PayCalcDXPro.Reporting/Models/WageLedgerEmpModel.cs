namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 賃金台帳（従業員別）。現行 R_給台帳従／R_賞台帳従 準拠。
/// 従業員1人を1ページとし、「項目（行）×支給回（列＝月／賞与回）」のクロス集計に年間合計列を持つ。
/// </summary>
public sealed record WageLedgerEmpModel
{
    public string 会社名 { get; init; } = "";
    /// <summary>右上の年表示（例「2026年」）。</summary>
    public string 年 { get; init; } = "";
    /// <summary>true=賞与台帳／false=給与台帳。</summary>
    public bool Is賞与 { get; init; }
    /// <summary>表頭左の区分ラベル（【給 与】／【賞 与】）。</summary>
    public string 区分ラベル { get; init; } = "";
    /// <summary>従業員ごとのページ。</summary>
    public IReadOnlyList<WageLedgerEmpPage> ページ { get; init; } = Array.Empty<WageLedgerEmpPage>();
}

/// <summary>従業員1人分（＝1ページ）。列は支給回、行は項目（<see cref="LedgerRow"/> を再利用）。</summary>
public sealed record WageLedgerEmpPage(
    string 所属,
    string 社員NO,
    string 氏名,
    string 締日,
    IReadOnlyList<WageLedgerEmpCol> 列,
    IReadOnlyList<LedgerRow> 行);

/// <summary>支給回の列見出し。上段＝支給年月／賞与回、下段＝支給日。</summary>
public sealed record WageLedgerEmpCol(string 上段, string 下段);
