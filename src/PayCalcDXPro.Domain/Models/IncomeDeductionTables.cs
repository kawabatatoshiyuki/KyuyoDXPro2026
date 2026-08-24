namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 年末調整の所得控除で用いる表。現行 T_基礎控除表 / T_配偶者控除 / T_生保控除１/２ 相当。
/// </summary>
public sealed record IncomeDeductionTables
{
    /// <summary>基礎控除（本人の合計所得＝給与所得控除後調整後に対する 控除額）。率0・加算額=控除額。</summary>
    public required IReadOnlyList<BracketRow> 基礎控除 { get; init; }

    /// <summary>配偶者（特別）控除の表（年齢ランク×配偶者所得→控除額1/2/3）。</summary>
    public required IReadOnlyList<SpouseDeductionRow> 配偶者控除 { get; init; }

    /// <summary>生命保険料控除（新制度。T_生保控除１）：保険料区分の 率(%/100)＋控除額。</summary>
    public required IReadOnlyList<BracketRow> 生保控除_新 { get; init; }

    /// <summary>生命保険料控除（旧制度。T_生保控除２）：保険料区分の 率(%/100)＋控除額。</summary>
    public required IReadOnlyList<BracketRow> 生保控除_旧 { get; init; }
}

/// <summary>配偶者控除表の1行。年齢ランク（0=共通/1=70歳未満/2=70歳以上）と配偶者所得区分、本人所得別の控除額。</summary>
public sealed record SpouseDeductionRow
{
    public decimal 配偶者所得下限 { get; init; }
    public decimal 配偶者所得上限 { get; init; } = decimal.MaxValue;
    /// <summary>本人合計所得 ≤900万 のときの控除額。</summary>
    public decimal 控除額1 { get; init; }
    /// <summary>本人合計所得 ≤950万 のときの控除額。</summary>
    public decimal 控除額2 { get; init; }
    /// <summary>本人合計所得 ≤1000万 のときの控除額。</summary>
    public decimal 控除額3 { get; init; }
    /// <summary>"0"=共通 / "1"=70歳未満 / "2"=70歳以上。</summary>
    public string 年齢ランク { get; init; } = "0";
}
