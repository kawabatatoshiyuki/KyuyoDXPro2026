using PayCalcDXPro.Data.YearEnd;

namespace PayCalcDXPro.Web.WithholdingSlip;

/// <summary>
/// 源泉徴収票の値組み立て（<see cref="WithholdingSlipBuilder"/>）に渡す、収集済みの入力一式。
/// LocalDatabaseService が 年末調整・従業員・会社・マイナンバー・役職・特定親族控除 などを集約して構築する。
/// </summary>
public sealed class WithholdingSlipInput
{
    // 画面入力
    public string 種別 { get; init; } = "";
    public string 元号 { get; init; } = "";
    public string 年 { get; init; } = "";

    // 年末調整（計算済み）
    public required YearEndEntry YearEnd { get; init; }
    /// <summary>年齢判定の基準年（現行 SYR_NEN＝処理年+1）。</summary>
    public int 判定年 { get; init; }

    // 従業員
    public string 受給者番号 { get; init; } = "";
    public string 役職名 { get; init; } = "";
    public string 氏名 { get; init; } = "";
    public string フリガナ { get; init; } = "";
    public string 住所1 { get; init; } = "";
    public string 住所2 { get; init; } = "";
    public string 生年月日 { get; init; } = "";

    // マイナンバー（復号済み）
    public string 本人番号 { get; init; } = "";
    public string 配偶者番号 { get; init; } = "";
    /// <summary>扶養親族1〜7（長さ7）。</summary>
    public string?[] 扶養番号 { get; init; } = new string?[7];

    // 会社（支払者）
    public string 会社名 { get; init; } = "";
    public string 会社所在地 { get; init; } = "";
    public string 会社電話 { get; init; } = "";
    public string 事業所番号 { get; init; } = "";

    // 派生値（呼び出し側で算定。未算定は 0／空でも動作）
    /// <summary>扶養親族1〜7 の特定親族特別控除額（現行 特定親族N控除。長さ7）。</summary>
    public int[] 特定親族控除 { get; init; } = new int[7];
    /// <summary>障害者控除（配偶者分）。カウント判定に使用（現行 障害配偶者控除）。</summary>
    public int 障害配偶者控除 { get; init; }
    /// <summary>住宅借入金等特別控除区分（1回目＝新築購入、M_コード 003 の値）。</summary>
    public string 新築購入区分名 { get; init; } = "";
    /// <summary>住宅借入金等特別控除区分（2回目＝増改築、M_コード 004 の値）。</summary>
    public string 増改築区分名 { get; init; } = "";
    /// <summary>所得金額調整控除の扶養者氏名（摘要の（調整）表示に使用）。</summary>
    public string 所得金額調整控除適用の扶養者氏名 { get; init; } = "";
    /// <summary>扶養親族1〜7 が扶養の収入要件を満たすか（摘要の（調整）判定に使用。長さ7）。</summary>
    public bool[] 扶養収入要件 { get; init; } = new bool[7];
}
