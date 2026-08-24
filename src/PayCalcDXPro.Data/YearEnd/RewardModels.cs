namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 報酬、料金、契約金及び賞金の支払調書（現行 T_年調報酬情報）の1件。
/// 明細は5区分（各：区分・細目・支払金額・内未払・源泉徴収税額・内未徴収税額）。
/// 区分1は「年調報酬区分」コード（1〜8号該当）、区分2〜5は生値（現行は空で保存）。
/// 区分名称を入力した場合は帳票の1区分目でコード名より優先される。
/// </summary>
public sealed class RewardRecord
{
    /// <summary>整理番号（主キー。0=新規＝登録時に MAX+1 で採番）。</summary>
    public int No { get; set; }
    /// <summary>支払調書を提出するもの（現行 列「提出」="1"/"0"）。</summary>
    public bool 提出 { get; set; }
    /// <summary>支払を受ける者の区分（現行 列「支払受者区分」="1"=法人／"2"=個人）。</summary>
    public string 支払受者区分 { get; set; } = "1";
    /// <summary>支払を受ける者の氏名又は名称（現行 列「名称」）。</summary>
    public string 名称 { get; set; } = "";
    /// <summary>支払を受ける者の住所（居所）又は所在地（現行 列「所在地」）。</summary>
    public string 所在地 { get; set; } = "";
    /// <summary>支払を受ける者の個人番号（12桁）又は法人番号（13桁）（現行 列「支払受者番号」）。</summary>
    public string 支払受者番号 { get; set; } = "";

    /// <summary>区分1〜5（区分1＝コード、区分2〜5＝生値）。</summary>
    public string[] 区分 { get; } = new string[5];
    /// <summary>細目1〜5。</summary>
    public string[] 細目 { get; } = new string[5];
    /// <summary>金額1〜5（未入力＝null＝空白。現行 intBlankZeroToNull 相当）。</summary>
    public int?[] 支払金額 { get; } = new int?[5];
    public int?[] 内未払 { get; } = new int?[5];
    public int?[] 源泉徴収税額 { get; } = new int?[5];
    public int?[] 内未徴収税額 { get; } = new int?[5];

    /// <summary>1区分目の区分名称（入力時はコード名より優先。現行 列「区分名称」）。</summary>
    public string 区分名称 { get; set; } = "";
    public string 摘要 { get; set; } = "";
}

/// <summary>報酬支払調書一覧の1行（整理番号＋名称）。</summary>
public sealed record RewardListItem(int No, string 名称);

/// <summary>年調報酬区分（コード＝1〜8号該当）の1件。</summary>
public sealed record RewardKubun(string コード, string 名称);
