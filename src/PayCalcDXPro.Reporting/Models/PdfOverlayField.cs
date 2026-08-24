namespace PayCalcDXPro.Reporting.Models;

/// <summary>オーバーレイ文字の水平揃え。</summary>
public enum PdfTextAlign
{
    Left,
    Right,
    Center,
}

/// <summary>
/// 背景PDF上に値を重ねる1項目の配置情報（mm単位、ページ左上原点）。
/// xlsxマスターのプレースホルダセルの位置・サイズから自動算出する（方式①）。
/// </summary>
public sealed record PdfOverlayField
{
    /// <summary>対象ページ（0始まり）。</summary>
    public int ページ { get; init; }
    public required double XMm { get; init; }
    public required double YMm { get; init; }
    /// <summary>セル（結合時は結合範囲）の幅・高さ。右寄せ・中央寄せ・縦中央に使用。</summary>
    public required double 幅Mm { get; init; }
    public required double 高Mm { get; init; }
    public double フォントPt { get; init; } = 9;
    public PdfTextAlign 揃え { get; init; } = PdfTextAlign.Left;
}
