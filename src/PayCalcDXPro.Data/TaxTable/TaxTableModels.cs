namespace PayCalcDXPro.Data.TaxTable;

/// <summary>月額表 別表第1（給与所得控除＝電算機計算の特例）。給与額の区間ごとに (A)×控除率% + 控除額。</summary>
public sealed class Table1Row
{
    public int No { get; set; }
    public int? 給与額自 { get; set; }
    public int? 給与額至 { get; set; }
    public double? 控除率 { get; set; }
    public int? 控除額 { get; set; }
}

/// <summary>月額表 別表第2（配偶者控除・扶養控除の1人あたり額）。1レコードのみ。</summary>
public sealed class Table2Row
{
    public int No { get; set; }
    public int? 配偶者控除 { get; set; }
    public int? 扶養控除 { get; set; }
}

/// <summary>月額表 別表第3（基礎控除）。給与額の区間ごとの控除額。</summary>
public sealed class Table3Row
{
    public int No { get; set; }
    public int? 給与額自 { get; set; }
    public int? 給与額至 { get; set; }
    public int? 控除額 { get; set; }
}

/// <summary>月額表 別表第4（税率速算）。課税給与額の区間ごとに (B)×税率% − 控除額。</summary>
public sealed class Table4Row
{
    public int No { get; set; }
    public int? 給与額自 { get; set; }
    public int? 給与額至 { get; set; }
    public double? 税率 { get; set; }
    public int? 控除額 { get; set; }
}

/// <summary>賞与の源泉徴収税額表（甲欄）。扶養親族0〜7人以上の前月給与境界額と賞与に乗ずる率。</summary>
public sealed class BonusKouRow
{
    public int No { get; set; }
    public int? 金額0人 { get; set; }
    public int? 金額1人 { get; set; }
    public int? 金額2人 { get; set; }
    public int? 金額3人 { get; set; }
    public int? 金額4人 { get; set; }
    public int? 金額5人 { get; set; }
    public int? 金額6人 { get; set; }
    public int? 金額7人以上 { get; set; }
    public double? 率 { get; set; }
}

/// <summary>賞与の源泉徴収税額表（乙欄）。前月給与境界額と賞与に乗ずる率。</summary>
public sealed class BonusOtsuRow
{
    public int No { get; set; }
    public int? 金額 { get; set; }
    public double? 率 { get; set; }
}

/// <summary>乙税第1表（乙欄 給与所得控除）。給与額区間ごとに 額×控除率% ＋ 控除額。現行 T_乙税第１表。</summary>
public sealed class OtsuTax1Row
{
    public int No { get; set; }
    public int? 給与額自 { get; set; }
    public int? 給与額至 { get; set; }
    public double? 控除率 { get; set; }
    public int? 控除額 { get; set; }
}

/// <summary>乙税第2表（乙欄 基礎控除相当の定額）。1レコードのみ。現行 T_乙税第２表。</summary>
public sealed class OtsuTax2Row
{
    public int No { get; set; }
    public int? 控除額 { get; set; }
}

/// <summary>乙税第3表（乙欄 税率速算）。給与額区間ごとに 額×控除率% − 控除額。現行 T_乙税第３表。</summary>
public sealed class OtsuTax3Row
{
    public int No { get; set; }
    public int? 給与額自 { get; set; }
    public int? 給与額至 { get; set; }
    public double? 控除率 { get; set; }
    public int? 控除額 { get; set; }
}

/// <summary>乙税階差（乙欄 計算基準額を求める刻み）。給与額区間ごとの階差。現行 T_乙税階差。</summary>
public sealed class OtsuKaisaRow
{
    public int No { get; set; }
    public int? 給与額自 { get; set; }
    public int? 給与額至 { get; set; }
    public int? 階差 { get; set; }
}

/// <summary>所得税（月額表）画面のデータ一式。別表第1〜第4、賞与甲・乙、乙税第1〜3表・階差。</summary>
public sealed class TaxTableBundle
{
    public List<Table1Row> 別表1 { get; set; } = new();
    public List<Table2Row> 別表2 { get; set; } = new();
    public List<Table3Row> 別表3 { get; set; } = new();
    public List<Table4Row> 別表4 { get; set; } = new();
    public List<BonusKouRow> 賞与甲 { get; set; } = new();
    public List<BonusOtsuRow> 賞与乙 { get; set; } = new();
    public List<OtsuTax1Row> 乙税第1 { get; set; } = new();
    public List<OtsuTax2Row> 乙税第2 { get; set; } = new();
    public List<OtsuTax3Row> 乙税第3 { get; set; } = new();
    public List<OtsuKaisaRow> 乙税階差 { get; set; } = new();
}
