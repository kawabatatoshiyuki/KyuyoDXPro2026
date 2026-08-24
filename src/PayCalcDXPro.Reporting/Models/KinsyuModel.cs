namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 現金支給金種表（現行 R_金種表）。現金支給の従業員ごとに支払金額と金種内訳（10000〜1円）を一覧する。
/// </summary>
public sealed record KinsyuModel
{
    public string 会社名 { get; init; } = "";
    /// <summary>給与＝「2025年12月分」／賞与＝「2026年7月賞与」。</summary>
    public string 支給見出し { get; init; } = "";
    public string 支給日 { get; init; } = "";
    /// <summary>2,000円札を含めるか。</summary>
    public bool Include2000 { get; init; }
    public IReadOnlyList<KinsyuRow> 明細 { get; init; } = Array.Empty<KinsyuRow>();
    public long 合計支払金額 { get; init; }
    /// <summary>金種ごとの合計枚数（10000,5000,2000,1000,500,100,50,10,5,1 の順で10要素）。</summary>
    public IReadOnlyList<long> 合計金種 { get; init; } = Array.Empty<long>();

    /// <summary>金種の額面（表示順）。</summary>
    public static readonly int[] 額面 = { 10000, 5000, 2000, 1000, 500, 100, 50, 10, 5, 1 };
}

/// <summary>金種表の1行。金種は <see cref="KinsyuModel.額面"/> と同順の10要素。</summary>
public sealed record KinsyuRow(string No, string 氏名, long 支払金額, IReadOnlyList<int> 金種);
