using PayCalcDXPro.Data.YearEnd;

namespace PayCalcDXPro.Web.WithholdingSlip;

/// <summary>
/// 給与所得に対する源泉徴収簿の値組み立て（<see cref="WithholdingLedgerBuilder"/>）に渡す、収集済みの入力一式。
/// 現行 R_徴収簿.createReport が参照する 会社・支給日情報・従業員・年末調整 を LocalDatabaseService が集約して構築する。
/// 個人番号は不要（現行 徴収簿は個人番号を印字しない）。定額減税欄(㉔-2〜㉔-4)は移植対象外（ブラウザ版に定額減税なし）。
/// </summary>
public sealed class WithholdingLedgerInput
{
    // 画面入力
    public string 元号 { get; init; } = "";
    public string 年 { get; init; } = "";

    // 年末調整（計算済み）
    public required YearEndEntry YearEnd { get; init; }
    /// <summary>年齢判定の基準年（現行 SYR_NEN＝処理年+1）。</summary>
    public int 判定年 { get; init; }

    // 従業員
    public string 所属 { get; init; } = "";   // 部門名
    public string 職名 { get; init; } = "";   // 役職名
    public string 整理番号 { get; init; } = ""; // No
    public string 郵便番号 { get; init; } = "";
    public string 住所 { get; init; } = "";
    public string 氏名 { get; init; } = "";
    public string フリガナ { get; init; } = "";
    public string 生年月日 { get; init; } = "";

    // 会社
    /// <summary>年末調整方法（M_会社情報。"3"＝最後の賞与で調整のとき過不足を賞与側へ）。</summary>
    public string 年末調整方法 { get; init; } = "1";

    // 支給日情報（M_支給日情報）：各月・各回の支給月/日（値なしは null）。
    public int?[] 支給月 { get; init; } = new int?[12];
    public int?[] 支給日 { get; init; } = new int?[12];
    public int?[] 賞与支給月 { get; init; } = new int?[4];
    public int?[] 賞与支給日 { get; init; } = new int?[4];

    /// <summary>扶養親族1〜7 の特定親族特別控除額（特定扶養／特定親族のカウント判定に使用。長さ7）。</summary>
    public int[] 特定親族控除 { get; init; } = new int[7];
}
