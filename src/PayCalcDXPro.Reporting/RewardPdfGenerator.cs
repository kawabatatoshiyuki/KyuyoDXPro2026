using System.Globalization;
using System.Text.RegularExpressions;
using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 報酬、料金、契約金及び賞金の支払調書（官製様式）を、背景PDF＋座標レイアウトへ値を重ねて生成する（方式①）。
/// 現行 R_報酬.createReport のセル代入を忠実に移植する（アレンジなし）。
/// 4面付け（1レコード＝1ページに同一内容4枚）は、同一マーカーが複数セルに現れる配置を全件描画して再現する。
/// 番号欄はテンプレ上「左列・上下2面のみ」に桁マーカー（_N番号_1）があり、右2面には無い＝番号は左2面だけ印字。
/// </summary>
public sealed class RewardPdfGenerator
{
    private static readonly Regex 受給者番号桁 = new(@"^\*\*支払を受ける者_個人番号又は法人番号_(\d{1,2})番号_1$", RegexOptions.Compiled);
    private static readonly Regex 支払者番号桁 = new(@"^\*\*支払者_個人番号又は法人番号_(\d{1,2})番号_1$", RegexOptions.Compiled);
    private static readonly Regex 区分マーカー = new(@"^\*\*区分_([1-5])区分_1$", RegexOptions.Compiled);
    private static readonly Regex 細目マーカー = new(@"^\*\*細目_([1-5])細目_1$", RegexOptions.Compiled);
    private static readonly Regex 金額マーカー = new(@"^\*\*源泉徴収税額_([1-5])(支払金額|内未払|源泉徴収税額|内未徴収税額)_1$", RegexOptions.Compiled);

    /// <param name="backgroundPdf">官製様式の背景PDF（xlsxのプレースホルダを消して印刷したもの）。</param>
    /// <param name="layout"><see cref="XlsxOverlayLayoutExtractor.ExtractAll"/> が返す (マーカー,配置) の全件。</param>
    public byte[] Generate(
        Stream backgroundPdf,
        IReadOnlyList<(string marker, PdfOverlayField field)> layout,
        RewardReportData d)
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

                var font = new XFont(JapaneseFontResolver.FamilyName, Math.Max(6.0, f.フォントPt));
                const double 内側余白Mm = 0.6;
                var rect = new XRect(
                    XUnit.FromMillimeter(f.XMm + 内側余白Mm).Point,
                    XUnit.FromMillimeter(f.YMm).Point,
                    XUnit.FromMillimeter(Math.Max(f.幅Mm - 内側余白Mm * 2, 2)).Point,
                    XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);

                // 番号の各桁は枠内中央に描く。それ以外は xlsx の揃えに従う。
                bool 桁マーカー = 受給者番号桁.IsMatch(marker) || 支払者番号桁.IsMatch(marker);
                PdfTextAlign 揃え = 桁マーカー ? PdfTextAlign.Center : f.揃え;
                gfx.DrawString(text, font, XBrushes.Black, rect, Format(揃え));
            }
        }

        using var outMs = new MemoryStream();
        doc.Save(outMs);
        return outMs.ToArray();
    }

    /// <summary>マーカー文字列を描画値へ解決する（空文字＝描画しない）。現行 createReport の代入内容に一致。</summary>
    private static string Resolve(string marker, RewardReportData d, string 受給者番号, string 支払者番号)
    {
        switch (marker)
        {
            case "**年数": return Wareki.年数(d.支給年);
            case "**支払を受ける者_住所(居所)又は所在地_1": return d.所在地;
            case "**支払を受ける者_氏名又は名称_1": return d.名称;
            case "**（摘要）_1": return d.摘要;
            case "**支払者_住所(居所)又は所在地_1": return d.会社所在地;
            case "**支払者_氏名又は名称_名称_1": return d.会社名;
            case "**支払者_氏名又は名称_電話_1": return d.会社電話;
        }

        // 区分（1区分目は呼び出し側で「区分名称 or コード名」を解決済み）。
        Match k = 区分マーカー.Match(marker);
        if (k.Success) return d.区分[int.Parse(k.Groups[1].Value) - 1] ?? "";

        // 細目
        Match s = 細目マーカー.Match(marker);
        if (s.Success) return d.細目[int.Parse(s.Groups[1].Value) - 1] ?? "";

        // 金額系（支払金額／内未払／源泉徴収税額／内未徴収税額）。null・0 は非表示（現行 intBlankZeroToNull）。
        Match m = 金額マーカー.Match(marker);
        if (m.Success)
        {
            int i = int.Parse(m.Groups[1].Value) - 1;
            int? v = m.Groups[2].Value switch
            {
                "支払金額" => d.支払金額[i],
                "内未払" => d.内未払[i],
                "源泉徴収税額" => d.源泉徴収税額[i],
                _ => d.内未徴収税額[i],
            };
            return Money(v);
        }

        // 支払を受ける者：個人番号又は法人番号（左2面のみ・_1番号_1が先頭＝index0）。
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

    /// <summary>13文字化された番号の N 桁目（1始まり・先頭が_1番号_1）。スペースは空文字＝非描画。</summary>
    private static string DigitAt(string padded, int n)
    {
        if (n < 1 || n > padded.Length) return "";
        char c = padded[n - 1];
        return c == ' ' ? "" : c.ToString();
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
