namespace PayCalcDXPro.Data.Rate;

/// <summary>
/// 保険料率（T_料率、1レコード No=1）。各保険の一般料率（事業所負担含む全体）と被保険者負担分。
/// 値は現行の表記（％・‰）をそのまま保持する。
/// </summary>
public sealed class RateSettings
{
    public double? 健康保険一般 { get; set; }
    public double? 健康保険本人 { get; set; }
    public double? 厚生年金一般 { get; set; }
    public double? 厚生年金本人 { get; set; }
    public double? 介護保険一般 { get; set; }
    public double? 介護保険本人 { get; set; }
    public double? 子育支援一般 { get; set; }
    public double? 子育支援本人 { get; set; }
    public double? 厚生年金基金一般 { get; set; }
    public double? 厚生年金基金本人 { get; set; }
    public double? 雇用保険一般 { get; set; }
    public double? 雇用保険一般以外 { get; set; }
}

/// <summary>保険料額表（T_料率保険料額表）の1行。等級・報酬月額の境界と各保険の被保険者負担額。</summary>
public sealed class RateTableRow
{
    public int No { get; set; }
    public int? 報酬月額自 { get; set; }
    public int? 報酬月額至 { get; set; }
    public int? 等級短期 { get; set; }
    public int? 等級長期 { get; set; }
    public int? 標準報酬月額短期 { get; set; }
    public int? 標準報酬月額長期 { get; set; }
    public double? 健康保険 { get; set; }
    public double? 厚生年金 { get; set; }
    public double? 介護保険 { get; set; }
    public double? 子育支援 { get; set; }
    public double? 厚生年金基金 { get; set; }
}

/// <summary>料率画面のデータ一式（料率＋保険料額表）。</summary>
public sealed class RateBundle
{
    public RateSettings 料率 { get; set; } = new();
    public List<RateTableRow> 額表 { get; set; } = new();
}
