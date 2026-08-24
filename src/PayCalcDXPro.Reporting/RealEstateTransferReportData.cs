namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 不動産等の譲受けの対価の支払調書（現行 R_不動産譲受）1枚分の描画データ。
/// VBReport createReport のセル代入元。明細は最大3行（物件の種類・物件の所在地・細目・数量・取得年月日・支払金額）。
/// 摘要は2行。摘要欄内に「あっせんをした者」を持つ。
/// </summary>
public sealed class RealEstateTransferReportData
{
    public int 支給年 { get; init; }

    // 支払を受ける者
    public string 所在地 { get; init; } = "";
    public string 名称 { get; init; } = "";
    public string 支払受者区分 { get; init; } = "2"; // "1"=法人(13桁) / "2"=個人(12桁)
    public string 支払受者番号 { get; init; } = "";

    // 明細（各3）。金額は null/0 で非表示（現行 intBlankZeroToNull）。物件の種類・支払金額は金額のある行のみ描画。
    public string[] 物件の種類 { get; init; } = new string[3];
    public string[] 物件の所在地 { get; init; } = new string[3];
    public string[] 細目 { get; init; } = new string[3];
    public string[] 数量 { get; init; } = new string[3];
    public DateTime?[] 取得年月日 { get; init; } = new DateTime?[3];
    public int?[] 支払金額 { get; init; } = new int?[3];

    // 摘要（2行）
    public string 摘要1 { get; init; } = "";
    public string 摘要2 { get; init; } = "";

    // 摘要欄内「あっせんをした者」
    public string あっせん所在地 { get; init; } = "";
    public string あっせん名称 { get; init; } = "";
    public string あっせん者区分 { get; init; } = "2"; // "1"=法人(13桁) / "2"=個人(12桁)
    public string あっせん者番号 { get; init; } = "";
    public DateTime? 支払確定年月日 { get; init; }
    public int? あっせん手数料 { get; init; }

    // 支払者（会社）
    public string 会社所在地 { get; init; } = "";
    public string 会社名 { get; init; } = "";
    public string 会社電話 { get; init; } = "";
    public string 事業所マイナンバー { get; init; } = "";
    public string 会社番号区分 { get; init; } = ""; // "1"=法人(13桁) / それ以外=個人(12桁)
}
