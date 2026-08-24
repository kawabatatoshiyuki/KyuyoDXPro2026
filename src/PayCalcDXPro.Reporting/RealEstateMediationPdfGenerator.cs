using System.Globalization;
using System.Text.RegularExpressions;
using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 不動産等の売買又は貸付けのあっせん手数料の支払調書（官製様式）を、背景PDF＋座標レイアウトへ値を重ねて生成する（方式①）。
/// 現行 R_不動産あっせん.createReport のセル代入を忠実に移植する（アレンジなし）。
/// A4横4面付け（1レコード＝1ページに同一内容4枚）は、同一マーカーが複数セルに現れる配置を全件描画して再現する。
/// 番号欄はテンプレ上「左列・上下2面のみ」に桁マーカーがあり、右2面には無い＝番号は左2面だけ印字。
/// マーカーは 譲受と異なり末尾 _1 を持たない（生テンプレのセル名そのまま）。
/// </summary>
public sealed class RealEstateMediationPdfGenerator
{
    private static readonly Regex 受給者番号桁 = new(@"^\*\*支払を受ける者_個人番号又は法人番号_(\d{1,2})番号$", RegexOptions.Compiled);
    private static readonly Regex 支払者番号桁 = new(@"^\*\*支払者_個人番号又は法人番号_(\d{1,2})番号$", RegexOptions.Compiled);
    private static readonly Regex 区分マーカー = new(@"^\*\*区分_([12])区分$", RegexOptions.Compiled);
    private static readonly Regex 支払確定年月日マーカー = new(@"^\*\*支払確定年月日_([12])支払確定年月日$", RegexOptions.Compiled);
    private static readonly Regex 支払金額マーカー = new(@"^\*\*支払金額_([12])支払金額$", RegexOptions.Compiled);
    private static readonly Regex 物件種類マーカー = new(@"^\*\*あっせんに係る不動産等_物件の種類_([12])物件の種類$", RegexOptions.Compiled);
    private static readonly Regex 物件所在地マーカー = new(@"^\*\*あっせんに係る不動産等_物件の所在地_([12])物件の所在地$", RegexOptions.Compiled);
    private static readonly Regex 数量マーカー = new(@"^\*\*あっせんに係る不動産等_数量_([12])数量$", RegexOptions.Compiled);
    private static readonly Regex 取引金額マーカー = new(@"^\*\*あっせんに係る不動産等_取引金額_([12])取引金額$", RegexOptions.Compiled);

    /// <param name="backgroundPdf">官製様式の背景PDF（xlsxのプレースホルダを消して印刷したもの）。</param>
    /// <param name="layout"><see cref="XlsxOverlayLayoutExtractor.ExtractAll"/> が返す (マーカー,配置) の全件。</param>
    public byte[] Generate(
        Stream backgroundPdf,
        IReadOnlyList<(string marker, PdfOverlayField field)> layout,
        RealEstateMediationReportData d)
    {
        JapaneseFontResolver.EnsureRegistered();

        using var src = new MemoryStream();
        backgroundPdf.CopyTo(src);
        src.Position = 0;
        using PdfDocument doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);

        // 番号は前処理（スペースパディング13文字）してから各桁へ配る（現行 inputNumber 準拠）。
        string 受給者番号 = NormalizeNumber(d.支払受者区分, d.支払受者番号);
        string 支払者番号 = NormalizeNumber(d.会社番号区分, d.事業所マイナンバー);

        foreach (var page in layout.GroupBy(x => x.field.ページ))
        {
            if (page.Key < 0 || page.Key >= doc.Pages.Count) continue;
            using XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[page.Key]);

            foreach ((string marker, PdfOverlayField f) in page)
            {
                string text = Resolve(marker, d, 受給者番号, 支払者番号);
                if (text.Length == 0) continue;

                var font = new XFont(JapaneseFontResolver.FamilyName, Math.Max(5.0, f.フォントPt));

                bool 桁マーカー = 受給者番号桁.IsMatch(marker) || 支払者番号桁.IsMatch(marker);
                bool 住所氏名名称 = marker.Contains("住所(居所)", StringComparison.Ordinal)
                    || marker.EndsWith("氏名又は名称", StringComparison.Ordinal)
                    || marker.EndsWith("氏名又は名称_名称", StringComparison.Ordinal);

                var rect = new XRect(
                    XUnit.FromMillimeter(f.XMm + 内側余白Mm).Point,
                    XUnit.FromMillimeter(f.YMm).Point,
                    XUnit.FromMillimeter(Math.Max(f.幅Mm - 内側余白Mm * 2, 2)).Point,
                    XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);

                // 列幅に収まらない値はフォントを自動縮小する（番号の各桁はレイアウト側でフォント率を掛けるため対象外）。
                if (物件種類マーカー.IsMatch(marker) || 物件所在地マーカー.IsMatch(marker) || 数量マーカー.IsMatch(marker))
                    font = FitFontToWidth(gfx, text, font, rect.Width, 明細縮小下限Pt);
                else if (住所氏名名称)
                    font = FitFontToWidth(gfx, text, font, rect.Width);

                PdfTextAlign 揃え = 桁マーカー ? PdfTextAlign.Center : f.揃え;
                gfx.DrawString(text, font, XBrushes.Black, rect, Format(揃え));
            }
        }

        using var outMs = new MemoryStream();
        doc.Save(outMs);
        return outMs.ToArray();
    }

    /// <summary>マーカー文字列を描画値へ解決する（空文字＝描画しない）。現行 createReport の代入内容に一致。</summary>
    private static string Resolve(string marker, RealEstateMediationReportData d, string 受給者番号, string 支払者番号)
    {
        switch (marker)
        {
            case "**年数": return Wareki.年数(d.支給年);
            case "**支払を受ける者_住所(居所)又は所在地": return d.所在地;
            case "**支払を受ける者_氏名又は名称": return d.名称;
            case "**（摘要）_摘要1": return Trunc摘要(d.摘要[0]);
            case "**（摘要）_摘要2": return Trunc摘要(d.摘要[1]);
            case "**（摘要）_摘要3": return Trunc摘要(d.摘要[2]);
            case "**支払者_住所(居所)又は所在地": return d.会社所在地;
            case "**支払者_氏名又は名称_名称": return d.会社名;
            case "**支払者_氏名又は名称_電話": return d.会社電話;
        }

        // 明細（区分・支払確定年月日・支払金額）。現行は全て無条件で代入（金額は intBlankZeroToNull で空欄化）。
        Match k = 区分マーカー.Match(marker);
        if (k.Success) return d.区分[int.Parse(k.Groups[1].Value) - 1] ?? "";

        Match pd = 支払確定年月日マーカー.Match(marker);
        if (pd.Success) { int i = int.Parse(pd.Groups[1].Value) - 1; return d.支払確定年月日[i] is { } dt ? Wareki.年月日(dt) : ""; }

        Match pm = 支払金額マーカー.Match(marker);
        if (pm.Success) return Money(d.支払金額[int.Parse(pm.Groups[1].Value) - 1]);

        // あっせんに係る不動産等（物件の種類・所在地・数量・取引金額）。
        Match t = 物件種類マーカー.Match(marker);
        if (t.Success) return d.物件の種類[int.Parse(t.Groups[1].Value) - 1] ?? "";

        Match b = 物件所在地マーカー.Match(marker);
        if (b.Success) return d.物件の所在地[int.Parse(b.Groups[1].Value) - 1] ?? "";

        Match q = 数量マーカー.Match(marker);
        if (q.Success) return d.数量[int.Parse(q.Groups[1].Value) - 1] ?? "";

        Match tk = 取引金額マーカー.Match(marker);
        if (tk.Success) return Money(d.取引金額[int.Parse(tk.Groups[1].Value) - 1]);

        // 支払を受ける者：個人番号又は法人番号（左2面のみ・_1番号が先頭＝index0）。
        Match r = 受給者番号桁.Match(marker);
        if (r.Success) return DigitAt(受給者番号, int.Parse(r.Groups[1].Value));

        // 支払者：個人番号又は法人番号
        Match p = 支払者番号桁.Match(marker);
        if (p.Success) return DigitAt(支払者番号, int.Parse(p.Groups[1].Value));

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

    /// <summary>13文字化された番号の N 桁目（1始まり・先頭が_1番号）。スペースは空文字＝非描画。</summary>
    private static string DigitAt(string padded, int n)
    {
        if (n < 1 || n > padded.Length) return "";
        char c = padded[n - 1];
        return c == ' ' ? "" : c.ToString();
    }

    // 明細文字列列の自動縮小の下限（狭い列に長い値を収める）。
    private const double 明細縮小下限Pt = 3.0;
    private const double 縮小安全率 = 0.97;
    private const double 内側余白Mm = 0.6;   // レイアウト側と一致

    /// <summary>文字列が列幅（point）に収まるようフォントを縮小して返す。収まる場合は元のフォントのまま。</summary>
    private static XFont FitFontToWidth(XGraphics gfx, string text, XFont font, double maxWidthPt, double floorPt = 5.0)
    {
        double w = gfx.MeasureString(text, font).Width;
        double target = maxWidthPt * 縮小安全率;
        if (w <= target || w <= 0) return font;
        double newSize = Math.Max(font.Size * (target / w), floorPt);
        return newSize < font.Size ? new XFont(JapaneseFontResolver.FamilyName, newSize) : font;
    }

    // 摘要は各行40文字までを表示し、残りはカットする（帳票の摘要欄幅に収めるため）。
    private const int 摘要最大文字数 = 40;
    private static string Trunc摘要(string? s)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= 摘要最大文字数 ? s : s.Substring(0, 摘要最大文字数));

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
