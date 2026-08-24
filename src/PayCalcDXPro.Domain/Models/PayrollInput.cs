namespace PayCalcDXPro.Domain.Models;

/// <summary>源泉徴収の区分（甲欄＝主たる給与／乙欄＝従たる給与）。</summary>
public enum 源泉区分
{
    甲,
    乙,
}

/// <summary>
/// 1人・1か月分の給与計算入力。
/// 現行 WinForms の F_給与処理 画面項目から、計算に必要なものだけを抽出した不変モデル。
/// （UI・DB に依存しないため、そのまま単体テストできる）
/// </summary>
public sealed record PayrollInput
{
    /// <summary>課税対象の支給合計（基本給＋課税手当＋課税通勤費 等）。</summary>
    public required decimal 課税支給合計 { get; init; }

    /// <summary>非課税支給合計（非課税通勤費 等）。源泉所得税の対象外だが差引支給には加算。</summary>
    public decimal 非課税支給合計 { get; init; }

    /// <summary>社会保険の標準報酬月額（健保・厚年の保険料計算基礎）。</summary>
    public decimal 標準報酬月額 { get; init; }

    /// <summary>源泉税額計算上の扶養親族等の数（控除対象配偶者を含む人数）。</summary>
    public int 扶養親族等の数 { get; init; }

    /// <summary>40歳以上65歳未満（介護保険第2号被保険者）か。</summary>
    public bool 介護保険対象 { get; init; }

    /// <summary>その他の控除（住民税・財形・控除手当 等）合計。</summary>
    public decimal その他控除合計 { get; init; }

    /// <summary>源泉徴収区分（既定は甲欄）。乙欄の場合は乙欄計算器が必要。</summary>
    public 源泉区分 源泉区分 { get; init; } = 源泉区分.甲;
}
