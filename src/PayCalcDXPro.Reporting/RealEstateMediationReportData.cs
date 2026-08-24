namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 不動産等の売買又は貸付けのあっせん手数料の支払調書（現行 R_不動産あっせん）1枚分の描画データ。
/// VBReport createReport のセル代入元。明細は2行（区分・支払確定年月日・支払金額）。
/// 「あっせんに係る不動産等」も2行（物件の種類・物件の所在地・数量・取引金額）。摘要は3行。
/// 譲受と違い「あっせんをした者」ブロックは持たない（支払を受ける者＋明細2行＋摘要3行のみ）。
/// 表示ラベルとDB列名が異なる点に注意：支払確定年月日＝DB「取得年月日」／取引金額＝DB「取得金額」。
/// </summary>
public sealed class RealEstateMediationReportData
{
    public int 支給年 { get; init; }

    // 支払を受ける者
    public string 所在地 { get; init; } = "";
    public string 名称 { get; init; } = "";
    public string 支払受者区分 { get; init; } = "2"; // "1"=法人(13桁) / "2"=個人(12桁)
    public string 支払受者番号 { get; init; } = "";

    // 明細（区分・支払確定年月日・支払金額）各2。金額は null/0 で非表示（現行 intBlankZeroToNull）。
    public string[] 区分 { get; init; } = new string[2];
    public DateTime?[] 支払確定年月日 { get; init; } = new DateTime?[2]; // DB列「取得年月日」
    public int?[] 支払金額 { get; init; } = new int?[2];

    // あっせんに係る不動産等（物件の種類・所在地・数量・取引金額）各2。
    public string[] 物件の種類 { get; init; } = new string[2];
    public string[] 物件の所在地 { get; init; } = new string[2];
    public string[] 数量 { get; init; } = new string[2];
    public int?[] 取引金額 { get; init; } = new int?[2]; // DB列「取得金額」

    // 摘要（3行）
    public string[] 摘要 { get; init; } = new string[3];

    // 支払者（会社）
    public string 会社所在地 { get; init; } = "";
    public string 会社名 { get; init; } = "";
    public string 会社電話 { get; init; } = "";
    public string 事業所マイナンバー { get; init; } = "";
    public string 会社番号区分 { get; init; } = ""; // "1"=法人(13桁) / それ以外=個人(12桁)
}
