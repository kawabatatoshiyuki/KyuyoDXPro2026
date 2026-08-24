namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 不動産等の譲受けの対価の支払調書（現行 T_不動産譲受 / F_年調_不動産譲受）の1件。
/// 明細は3行（各：物件の種類・物件の所在地・細目・数量・取得年月日・支払金額）。摘要は2行。
/// 摘要欄内に「あっせんをした者」を持つ（不動産と同構造）。
/// </summary>
public sealed class RealEstateTransferRecord
{
    /// <summary>整理番号（主キー。0=新規＝登録時に MAX+1 で採番）。</summary>
    public int No { get; set; }
    /// <summary>調書不要（現行 列「不要」="1"/"0"）。</summary>
    public bool 不要 { get; set; }

    /// <summary>支払を受ける者の区分（現行 列「支払受者区分」="1"=法人／"2"=個人）。</summary>
    public string 支払受者区分 { get; set; } = "1";
    /// <summary>支払を受ける者の氏名又は名称（現行 列「名称」）。</summary>
    public string 名称 { get; set; } = "";
    /// <summary>支払を受ける者の住所（居所）又は所在地（現行 列「所在地」）。</summary>
    public string 所在地 { get; set; } = "";
    /// <summary>支払を受ける者の個人番号（12桁）又は法人番号（13桁）（現行 列「支払受者番号」）。</summary>
    public string 支払受者番号 { get; set; } = "";

    /// <summary>物件の種類1〜3（地代・借地権・建物・船舶・航空機 等）。</summary>
    public string[] 物件の種類 { get; } = new string[3];
    /// <summary>物件の所在地1〜3。</summary>
    public string[] 物件の所在地 { get; } = new string[3];
    /// <summary>細目1〜3。</summary>
    public string[] 細目 { get; } = new string[3];
    /// <summary>数量1〜3（"100㎡" 等の自由文字列）。</summary>
    public string[] 数量 { get; } = new string[3];
    /// <summary>取得年月日1〜3（実在日付。DBには ISO "yyyy-MM-dd" のTEXTで保存。未入力＝null）。</summary>
    public DateTime?[] 取得年月日 { get; } = new DateTime?[3];
    /// <summary>支払金額1〜3（未入力＝null＝空白）。</summary>
    public int?[] 支払金額 { get; } = new int?[3];

    /// <summary>摘要1〜2（現行 列「摘要1」「摘要2」）。</summary>
    public string[] 摘要 { get; } = new string[2];

    // 摘要欄内「あっせんをした者」
    public string あっせん名称 { get; set; } = "";
    public string あっせん所在地 { get; set; } = "";
    /// <summary>支払確定年月日（現行 列「支払確定年月日」＝日付文字列。未入力＝null）。</summary>
    public DateTime? 支払確定年月日 { get; set; }
    public int? あっせん手数料 { get; set; }
    /// <summary>あっせんをした者の区分（現行 列「あっせん者区分」="1"=法人／"2"=個人）。</summary>
    public string あっせん者区分 { get; set; } = "1";
    /// <summary>あっせんをした者の個人番号（12桁）又は法人番号（13桁）（現行 列「あっせん者番号」）。</summary>
    public string あっせん者番号 { get; set; } = "";
}

/// <summary>不動産譲受支払調書一覧の1行（整理番号＋名称）。</summary>
public sealed record RealEstateTransferListItem(int No, string 名称);
