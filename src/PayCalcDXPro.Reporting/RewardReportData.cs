namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 報酬、料金、契約金及び賞金の支払調書（現行 R_報酬）1枚分の描画データ。
/// VBReport createReport のセル代入元。明細は最大5区分。
/// 区分[0]（1区分目）は「区分名称 or 年調報酬区分コード名」を解決済みの文字列を入れる（現行の分岐を呼び出し側で反映）。
/// </summary>
public sealed class RewardReportData
{
    public int 支給年 { get; init; }

    // 支払を受ける者
    public string 所在地 { get; init; } = "";
    public string 名称 { get; init; } = "";
    public string 支払受者区分 { get; init; } = "2"; // "1"=法人(13桁) / "2"=個人(12桁)
    public string 支払受者番号 { get; init; } = "";

    // 明細（各5）。金額は null/0 で非表示（現行 intBlankZeroToNull）。
    public string[] 区分 { get; init; } = new string[5];
    public string[] 細目 { get; init; } = new string[5];
    public int?[] 支払金額 { get; init; } = new int?[5];
    public int?[] 内未払 { get; init; } = new int?[5];
    public int?[] 源泉徴収税額 { get; init; } = new int?[5];
    public int?[] 内未徴収税額 { get; init; } = new int?[5];

    // 摘要
    public string 摘要 { get; init; } = "";

    // 支払者（会社）
    public string 会社所在地 { get; init; } = "";
    public string 会社名 { get; init; } = "";
    public string 会社電話 { get; init; } = "";
    public string 事業所マイナンバー { get; init; } = "";
    public string 会社番号区分 { get; init; } = ""; // "1"=法人(13桁) / それ以外=個人(12桁)
}
