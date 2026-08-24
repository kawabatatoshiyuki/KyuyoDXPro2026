namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 退職所得の源泉徴収票・特別徴収票（現行 R_退職源泉）1件分の描画データ。
/// 現行 T_退職源泉情報＋M_会社情報＋M_支給日情報.支給年 に一致。区分1/2/3＝所得税法201条
/// 第1項第1号／第1項第2号／第3項の適用分。値は VBReport のセル代入と同じ内容を渡す。
/// </summary>
public sealed class RetirementWithholdingReportData
{
    /// <summary>支給年（西暦）。様式の「令和○年分」の年数に和暦変換して使う。</summary>
    public int 支給年 { get; init; }

    // 支払を受ける者
    public string 氏名 { get; init; } = "";
    /// <summary>個人番号（12桁。桁数が12でない場合は各桁を空欄にする＝現行仕様）。</summary>
    public string 個人番号 { get; init; } = "";
    public string 住所 { get; init; } = "";
    public string 年始住所 { get; init; } = "";

    // 区分1/2/3 の金額（円）。現行はDB値をそのまま代入（0も0として表示）。
    public int[] 支払金額 { get; init; } = new int[3];
    public int[] 源泉徴収税額 { get; init; } = new int[3];
    public int[] 市町村民税 { get; init; } = new int[3];
    public int[] 道府県民税 { get; init; } = new int[3];

    public int 退職所得控除額 { get; init; }
    public string 勤続年数 { get; init; } = "";
    /// <summary>就職年月日（yyyy/MM/dd。空可）。様式は和暦（令和○年○月○日）で表示。</summary>
    public string 就職年月日 { get; init; } = "";
    public string 退職年月日 { get; init; } = "";
    public string 摘要 { get; init; } = "";

    // 支払者（会社情報）
    public string 会社所在地 { get; init; } = "";
    public string 会社名 { get; init; } = "";
    public string 会社電話 { get; init; } = "";
    /// <summary>番号区分（"1"=個人番号13桁扱い／それ以外=法人番号12桁扱い。現行 M_会社情報.番号区分）。</summary>
    public string 番号区分 { get; init; } = "";
    /// <summary>事業所マイナンバー（番号区分により13桁／12桁）。</summary>
    public string 事業所マイナンバー { get; init; } = "";
}
