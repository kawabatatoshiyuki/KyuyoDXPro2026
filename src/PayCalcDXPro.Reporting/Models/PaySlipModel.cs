namespace PayCalcDXPro.Reporting.Models;

/// <summary>
/// 支払明細書（給与・賞与）PDFの描画モデル。現行 R_給明細／R_賞明細 の固定セルレイアウトを踏襲する。
/// 支給／控除／勤怠は「5列×固定行」のグリッド。各セルは項目名＋金額（空スロットは空セル）。
/// 支給・控除の末尾セルが合計（総支給額／総控除額）。賞与は勤怠なし。
/// </summary>
public sealed record PaySlipModel
{
    public required string タイトル { get; init; }      // 給与支給明細書 / 賞与支給明細書
    public string 対象表示 { get; init; } = "";          // 例「2023　12」
    public string 支給日表示 { get; init; } = "";        // 例「2024/1/5」
    public string 締日表示 { get; init; } = "";          // 例「前月末日締め」。賞与は空。
    public required string 会社名 { get; init; }

    public string 社員番号 { get; init; } = "";
    public string 従業員名 { get; init; } = "";
    public string 所属 { get; init; } = "";

    /// <summary>支給グリッド（固定スロット順。末尾に総支給額の合計セル）。1行＝5セル。</summary>
    public IReadOnlyList<PaySlipLine> 支給 { get; init; } = Array.Empty<PaySlipLine>();
    /// <summary>控除グリッド（固定スロット順。末尾に総控除額の合計セル）。</summary>
    public IReadOnlyList<PaySlipLine> 控除 { get; init; } = Array.Empty<PaySlipLine>();
    /// <summary>勤怠グリッド（固定スロット順）。賞与は空。</summary>
    public IReadOnlyList<PaySlipFact> 勤怠 { get; init; } = Array.Empty<PaySlipFact>();

    public int 年末調整還付金 { get; init; }
    public int 年末調整徴収金 { get; init; }
    public int 差引支給額 { get; init; }

    public string 備考 { get; init; } = "";
}

/// <summary>明細1セル。名称が空＆金額nullなら空セル。合計=太枠強調。</summary>
public sealed record PaySlipLine(string 名称, int? 金額, bool 合計 = false);

/// <summary>勤怠1セル（名称＋表示値）。名称が空なら空セル。値2は残業の(60h超)用（2明細レイアウトで使用）。</summary>
public sealed record PaySlipFact(string 名称, string 値, string? 値2 = null);
