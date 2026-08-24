using System.Globalization;
using System.Text.RegularExpressions;
using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 不動産等の譲受けの対価の支払調書（官製様式）を、背景PDF＋座標レイアウトへ値を重ねて生成する（方式①）。
/// 現行 R_不動産譲受.createReport のセル代入を忠実に移植する（アレンジなし）。
/// 4面付け（1レコード＝1ページに同一内容4枚）は、同一マーカーが複数セルに現れる配置を全件描画して再現する。
/// 番号欄はテンプレ上「左列・上下2面のみ」に桁マーカー（_N番号_1）があり、右2面には無い＝番号は左2面だけ印字。
/// </summary>
public sealed class RealEstateTransferPdfGenerator
{
    private static readonly Regex 受給者番号桁 = new(@"^\*\*支払を受ける者_個人番号又は法人番号_(\d{1,2})番号_1$", RegexOptions.Compiled);
    private static readonly Regex 支払者番号桁 = new(@"^\*\*支払者_個人番号又は法人番号_(\d{1,2})番号_1$", RegexOptions.Compiled);
    private static readonly Regex あっせん番号桁 = new(@"^\*\*（摘要）_あっせんをした者_個人番号又は法人番号_(\d{1,2})番号_1$", RegexOptions.Compiled);
    private static readonly Regex 物件種類マーカー = new(@"^\*\*物件の種類_([1-3])物件の種類_1$", RegexOptions.Compiled);
    private static readonly Regex 物件所在地マーカー = new(@"^\*\*物件の所在地_([1-3])物件の所在地_1$", RegexOptions.Compiled);
    private static readonly Regex 細目マーカー = new(@"^\*\*細目_([1-3])細目_1$", RegexOptions.Compiled);
    private static readonly Regex 数量マーカー = new(@"^\*\*数量_([1-3])数量_1$", RegexOptions.Compiled);
    private static readonly Regex 取得年月日マーカー = new(@"^\*\*取得年月日_([1-3])取得年月日_1$", RegexOptions.Compiled);
    private static readonly Regex 支払金額マーカー = new(@"^\*\*支払金額_([1-3])支払金額_1$", RegexOptions.Compiled);

    /// <param name="backgroundPdf">官製様式の背景PDF（xlsxのプレースホルダを消して印刷したもの）。</param>
    /// <param name="layout"><see cref="XlsxOverlayLayoutExtractor.ExtractAll"/> が返す (マーカー,配置) の全件。</param>
    public byte[] Generate(
        Stream backgroundPdf,
        IReadOnlyList<(string marker, PdfOverlayField field)> layout,
        RealEstateTransferReportData d)
    {
        JapaneseFontResolver.EnsureRegistered();

        using var src = new MemoryStream();
        backgroundPdf.CopyTo(src);
        src.Position = 0;
        using PdfDocument doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);

        // 番号は前処理（スペースパディング13文字）してから各桁へ配る（現行 inputNumber 準拠）。
        string 受給者番号 = NormalizeNumber(d.支払受者区分, d.支払受者番号);
        string 支払者番号 = NormalizeNumber(d.会社番号区分, d.事業所マイナンバー);
        string あっせん番号 = NormalizeNumber(d.あっせん者区分, d.あっせん者番号);

        foreach (var page in layout.GroupBy(x => x.field.ページ))
        {
            if (page.Key < 0 || page.Key >= doc.Pages.Count) continue;
            using XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[page.Key]);

            foreach ((string marker, PdfOverlayField f) in page)
            {
                // あっせんをした者の個人番号又は法人番号は左2面のみ印字（右2面＝X>145mm は受給者・支払者と同様に非印字）。
                if (f.XMm > 145.0 && あっせん番号桁.IsMatch(marker)) continue;

                string text = Resolve(marker, d, 受給者番号, 支払者番号, あっせん番号);
                if (text.Length == 0) continue;

                var font = new XFont(JapaneseFontResolver.FamilyName, Math.Max(5.0, f.フォントPt));

                bool 桁マーカー = 受給者番号桁.IsMatch(marker) || 支払者番号桁.IsMatch(marker) || あっせん番号桁.IsMatch(marker);
                bool 住所氏名名称 = marker.Contains("住所(居所)", StringComparison.Ordinal)
                    || marker.EndsWith("氏名又は名称_1", StringComparison.Ordinal)
                    || marker.EndsWith("氏名又は名称_名称_1", StringComparison.Ordinal);

                var rect = new XRect(
                    XUnit.FromMillimeter(f.XMm + 内側余白Mm).Point,
                    XUnit.FromMillimeter(f.YMm).Point,
                    XUnit.FromMillimeter(Math.Max(f.幅Mm - 内側余白Mm * 2, 2)).Point,
                    XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);

                // 列幅に収まらない値はフォントを自動縮小する（番号の各桁はレイアウト側でフォント率を掛けるため対象外）。
                // ・明細の文字列列（物件の種類・物件の所在地・細目・数量）は列幅が狭く長い値が隣列へはみ出すため下限を下げる。
                // ・住所/氏名/名称（特にあっせんは幅が狭い）は枠内へ収める。
                if (物件種類マーカー.IsMatch(marker) || 物件所在地マーカー.IsMatch(marker)
                    || 細目マーカー.IsMatch(marker) || 数量マーカー.IsMatch(marker))
                    font = FitFontToWidth(gfx, text, font, rect.Width, 明細縮小下限Pt);
                else if (住所氏名名称)
                    font = FitFontToWidth(gfx, text, font, rect.Width);

                // 番号の各桁は枠内中央に描く。それ以外は xlsx の揃えに従う。
                PdfTextAlign 揃え = 桁マーカー ? PdfTextAlign.Center : f.揃え;
                gfx.DrawString(text, font, XBrushes.Black, rect, Format(揃え));
            }
        }

        using var outMs = new MemoryStream();
        doc.Save(outMs);
        return outMs.ToArray();
    }

    /// <summary>マーカー文字列を描画値へ解決する（空文字＝描画しない）。現行 createReport の代入内容に一致。</summary>
    private static string Resolve(string marker, RealEstateTransferReportData d, string 受給者番号, string 支払者番号, string あっせん番号)
    {
        switch (marker)
        {
            case "**年数": return Wareki.年数(d.支給年);
            case "**支払を受ける者_住所(居所)又は所在地_1": return d.所在地;
            case "**支払を受ける者_氏名又は名称_1": return d.名称;
            case "**（摘要）_摘要1_1": return d.摘要1;
            case "**（摘要）_摘要2_1": return d.摘要2;
            case "**（摘要）_あっせんをした者_住所(居所)又は所在地_1": return d.あっせん所在地;
            case "**（摘要）_あっせんをした者_氏名又は名称_1": return d.あっせん名称;
            case "**（摘要）_あっせんをした者_支払確定年月日_1": return d.支払確定年月日 is { } dt ? Wareki.年月日(dt) : "";
            case "**（摘要）_あっせんをした者_あっせん手数料_1": return Money(d.あっせん手数料);
            case "**支払者_住所(居所)又は所在地_1": return d.会社所在地;
            case "**支払者_氏名又は名称_名称_1": return d.会社名;
            case "**支払者_氏名又は名称_電話_1": return d.会社電話;
        }

        // 明細（物件の種類・支払金額は金額のある行のみ。物件の所在地・細目・数量・取得年月日は常に）。
        Match t = 物件種類マーカー.Match(marker);
        if (t.Success) { int i = int.Parse(t.Groups[1].Value) - 1; return d.支払金額[i].HasValue ? (d.物件の種類[i] ?? "") : ""; }

        Match b = 物件所在地マーカー.Match(marker);
        if (b.Success) return d.物件の所在地[int.Parse(b.Groups[1].Value) - 1] ?? "";

        Match s = 細目マーカー.Match(marker);
        if (s.Success) return d.細目[int.Parse(s.Groups[1].Value) - 1] ?? "";

        Match q = 数量マーカー.Match(marker);
        if (q.Success) return d.数量[int.Parse(q.Groups[1].Value) - 1] ?? "";

        Match y = 取得年月日マーカー.Match(marker);
        if (y.Success) { int i = int.Parse(y.Groups[1].Value) - 1; return d.取得年月日[i] is { } dt ? Wareki.年月日(dt) : ""; }

        // 金額系（支払金額）。null・0 は非表示（現行 intBlankZeroToNull）。
        Match m = 支払金額マーカー.Match(marker);
        if (m.Success) return Money(d.支払金額[int.Parse(m.Groups[1].Value) - 1]);

        // 支払を受ける者：個人番号又は法人番号（左2面のみ・_1番号_1が先頭＝index0）。
        Match r = 受給者番号桁.Match(marker);
        if (r.Success) return DigitAt(受給者番号, int.Parse(r.Groups[1].Value));

        // 支払者：個人番号又は法人番号
        Match p = 支払者番号桁.Match(marker);
        if (p.Success) return DigitAt(支払者番号, int.Parse(p.Groups[1].Value));

        // あっせんをした者：個人番号又は法人番号
        Match a = あっせん番号桁.Match(marker);
        if (a.Success) return DigitAt(あっせん番号, int.Parse(a.Groups[1].Value));

        return "";
    }

    /// <summary>
    /// 現行 inputNumber 準拠。番号が数値かつ（法人=13桁／個人=12桁）のときのみ有効化し、
    /// 12桁は先頭にスペースを付けて13文字化する。無効な番号は全桁スペース（＝非表示）。
    /// </summary>
    private static string NormalizeNumber(string kubun, string number)
    {
        number ??= "";
        bool 法人 = kubun == "1";
        if (long.TryParse(number, out _) && ((法人 && number.Length == 13) || (!法人 && number.Length == 12)))
        {
            if (number.Length == 12) number = " " + number; // 12桁→先頭スペースで13文字
            return number;
        }
        return new string(' ', 13);
    }

    /// <summary>13文字化された番号の N 桁目（1始まり・先頭が_1番号_1）。スペースは空文字＝非描画。</summary>
    private static string DigitAt(string padded, int n)
    {
        if (n < 1 || n > padded.Length) return "";
        char c = padded[n - 1];
        return c == ' ' ? "" : c.ToString();
    }

    // 明細文字列列の自動縮小の下限。狭い列（細目≒11mm）に長い値（例「鉄筋コンクリート造」9文字）を
    // 収めるには 3.4pt 前後まで縮める必要があるため、既定の 5.0pt では頭打ちになる。Excel の「縮小して全体を
    // 表示」に倣い、明細列だけ下限を下げて確実に列内へ収める（現実の値は多くが十分大きく縮小は発生しない）。
    private const double 明細縮小下限Pt = 3.0;

    // 測定値と実描画の僅かな差でも右隣へ食い込まないよう、収める目標幅に安全マージンを掛ける。
    private const double 縮小安全率 = 0.97;

    private const double 内側余白Mm = 0.6;   // 通常セルの左右内側余白（レイアウト側と一致）

    /// <summary>
    /// 文字列が列幅（point）に収まるようフォントを縮小して返す。収まる場合は元のフォントのまま。
    /// </summary>
    private static XFont FitFontToWidth(XGraphics gfx, string text, XFont font, double maxWidthPt, double floorPt = 5.0)
    {
        double w = gfx.MeasureString(text, font).Width;
        double target = maxWidthPt * 縮小安全率;
        if (w <= target || w <= 0) return font;
        double newSize = Math.Max(font.Size * (target / w), floorPt);
        return newSize < font.Size ? new XFont(JapaneseFontResolver.FamilyName, newSize) : font;
    }

    // 金額列は Excel numFmt 38（#,##0）。現行 intBlankZeroToNull により 0・null は空欄。
    private static string Money(int? v) => (v is null or 0) ? "" : v.Value.ToString("#,##0", CultureInfo.InvariantCulture);

    private static XStringFormat Format(PdfTextAlign align) => new()
    {
        Alignment = align switch
        {
            PdfTextAlign.Right => XStringAlignment.Far,
            PdfTextAlign.Center => XStringAlignment.Center,
            _ => XStringAlignment.Near,
        },
        LineAlignment = XLineAlignment.Center,
    };
}
