namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 法定調書合計表の提出者欄（現行 M_会社情報＋支給年）。各値は様式の対応セルへ流し込む。
/// </summary>
public sealed record 法定調書提出者情報
{
    /// <summary>
    /// 和暦の年数のみ（例「7」）。元号「令和」は様式テンプレ側に固定で入っているため重複させない。
    /// 現行は支給年を adToJapCal(FORMAT_1) で年数へ変換して設定。
    /// </summary>
    public string? 年 { get; init; }
    public string? 署番号 { get; init; }
    public string? 税務署名 { get; init; }
    public string? 事業種目 { get; init; }
    public string? 整理番号 { get; init; }
    public string? 所在地 { get; init; }
    public string? 電話番号 { get; init; }
    public string? 会社名 { get; init; }
    public string? 会社名カナ { get; init; }
    public string? 代表者名 { get; init; }
    public string? 代表者カナ { get; init; }
    /// <summary>事業所のマイナンバー（個人12桁）または法人番号（13桁）。</summary>
    public string? 事業所番号 { get; init; }
    /// <summary>番号区分（現行：1=法人番号13桁 / 2=個人番号12桁）。</summary>
    public string? 番号区分 { get; init; }
}
