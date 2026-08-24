namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 不動産等の売買又は貸付けのあっせん手数料の支払調書（現行 T_不動産あっせん情報 / F_年調_不動産あっせん）の1件。
/// 明細は2行（各：区分・取得年月日・支払金額／物件の種類区分・物件の所在地・数量・取得金額）。摘要は3行。
/// 譲受と違い「あっせんをした者」ブロックは持たない（支払を受ける者＋明細2行＋摘要3行のみ）。
/// </summary>
public sealed class RealEstateMediationRecord
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

    /// <summary>区分1〜2（譲渡・譲受け・貸付け・借受け）。</summary>
    public string[] 区分 { get; } = new string[2];
    /// <summary>取得年月日1〜2（実在日付。DBには ISO "yyyy-MM-dd" のTEXTで保存。未入力＝null）。</summary>
    public DateTime?[] 取得年月日 { get; } = new DateTime?[2];
    /// <summary>支払金額1〜2（未入力＝null＝空白）。</summary>
    public int?[] 支払金額 { get; } = new int?[2];

    /// <summary>物件の種類区分1〜2（土地・借地権・地役権・建物）。</summary>
    public string[] 物件の種類区分 { get; } = new string[2];
    /// <summary>物件の所在地1〜2。</summary>
    public string[] 物件の所在地 { get; } = new string[2];
    /// <summary>数量1〜2（"100㎡" 等の自由文字列）。</summary>
    public string[] 数量 { get; } = new string[2];
    /// <summary>取得金額1〜2（未入力＝null＝空白）。</summary>
    public int?[] 取得金額 { get; } = new int?[2];

    /// <summary>摘要1〜3（現行 列「摘要1」「摘要2」「摘要3」）。</summary>
    public string[] 摘要 { get; } = new string[3];
}

/// <summary>不動産あっせん支払調書一覧の1行（整理番号＋名称）。</summary>
public sealed record RealEstateMediationListItem(int No, string 名称);
