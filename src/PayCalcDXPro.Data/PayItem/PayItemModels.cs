namespace PayCalcDXPro.Data.PayItem;

/// <summary>項目名スロット（控除項目・賞与項目など名称のみ）。</summary>
public class ItemName
{
    public string 名称 { get; set; } = "";
}

/// <summary>給与支給の手当項目（名称＋非課税区分）。</summary>
public sealed class AllowanceItem : ItemName
{
    /// <summary>非課税ならtrue（給与支給非課税区分＝"1"）。</summary>
    public bool 非課税 { get; set; }
}

/// <summary>
/// 支給・控除項目の設定（M_支給控除項目設定、1レコード）。
/// 固定項目（基本給・各社会保険など）はラベル表示のみで編集対象外。
/// 編集対象は手当/控除のユーザー項目名と非課税区分、項目名印字フラグ。
/// </summary>
public sealed class PayItemSettings
{
    public List<AllowanceItem> 給与手当 { get; set; } = new();   // 12
    public List<ItemName> 給与控除 { get; set; } = new();         // 9
    public List<ItemName> 賞与手当 { get; set; } = new();         // 16
    public List<ItemName> 賞与控除 { get; set; } = new();         // 9
    public bool 項目名印字 { get; set; }

    /// <summary>固定項目の表示ラベル（読み取り専用・参考表示用）。列名→ラベル。</summary>
    public Dictionary<string, string> 固定ラベル { get; set; } = new();
}
