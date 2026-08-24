namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 退職所得の源泉徴収票・特別徴収票（現行 T_退職源泉情報）の1件。
/// 区分1/2/3＝所得税法201条 第1項第1号／第1項第2号／第3項 の適用分。
/// </summary>
public sealed class RetirementWithholdingRecord
{
    /// <summary>整理番号（主キー。0=新規＝登録時に MAX+1 で採番）。</summary>
    public int No { get; set; }
    /// <summary>支払調書を提出するもの（現行 列「提出」="1"/"0"）。</summary>
    public bool 提出 { get; set; }
    /// <summary>役職・氏名（現行 列「氏名」）。</summary>
    public string 氏名 { get; set; } = "";
    public string 個人番号 { get; set; } = "";
    /// <summary>住所又は居所（現行 列「住所」）。</summary>
    public string 住所 { get; set; } = "";
    /// <summary>1月1日の住所（現行 列「年始住所」）。</summary>
    public string 年始住所 { get; set; } = "";

    /// <summary>区分1/2/3 の支払金額。</summary>
    public int[] 支払金額 { get; } = new int[3];
    public int[] 源泉徴収税額 { get; } = new int[3];
    public int[] 市町村民税 { get; } = new int[3];
    public int[] 道府県民税 { get; } = new int[3];

    public int 退職所得控除額 { get; set; }
    public string 勤続年数 { get; set; } = "";
    public string 就職年月日 { get; set; } = "";
    public string 退職年月日 { get; set; } = "";
    public string 摘要 { get; set; } = "";
}

/// <summary>退職源泉一覧の1行（整理番号＋氏名）。</summary>
public sealed record RetirementWithholdingListItem(int No, string 氏名);

/// <summary>退職者選択の候補（中途退職／死亡退職の従業員）。表示名は「役職名　氏名」。</summary>
public sealed record RetiredEmployeeItem(int Number, string No, string 表示名);

/// <summary>退職者選択で取り込む値（役職氏名・個人番号・住所・退職年月日）。</summary>
public sealed record RetiredEmployeePrefill(string 役職氏名, string 個人番号, string 住所, string 退職年月日);
