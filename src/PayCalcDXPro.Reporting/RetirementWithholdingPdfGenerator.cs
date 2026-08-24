using System.Globalization;
using System.Text.RegularExpressions;
using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 退職所得の源泉徴収票・特別徴収票（官製様式）を、背景PDF＋座標レイアウトへ値を重ねて生成する（方式①）。
/// 現行 R_退職源泉.createReport のセル代入を忠実に移植する（アレンジなし）。
/// 4面付け（1レコード＝1ページに同一内容4枚）は、同一マーカーが4か所に現れる配置を全件描画して再現する。
/// </summary>
public sealed class RetirementWithholdingPdfGenerator
{
    private static readonly Regex 受給者番号桁 = new(@"^\*\*支払を受ける者_個人番号_(\d{1,2})桁$", RegexOptions.Compiled);
    private static readonly Regex 支払者番号桁 = new(@"^\*\*支払者_個人番号又は法人番号_(\d{1,2})桁$", RegexOptions.Compiled);

    /// <param name="backgroundPdf">官製様式の背景PDF（xlsxのプレースホルダを消して印刷したもの）。</param>
    /// <param name="layout"><see cref="XlsxOverlayLayoutExtractor.ExtractAll"/> が返す (マーカー,配置) の全件。</param>
    public byte[] Generate(
        Stream backgroundPdf,
        IReadOnlyList<(string marker, PdfOverlayField field)> layout,
        RetirementWithholdingReportData d)
    {
        JapaneseFontResolver.EnsureRegistered();

        using var src = new MemoryStream();
        backgroundPdf.CopyTo(src);
        src.Position = 0;
        using PdfDocument doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);

        foreach (var page in layout.GroupBy(x => x.field.ページ))
        {
            if (page.Key < 0 || page.Key >= doc.Pages.Count) continue;
            using XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[page.Key]);

            foreach ((string marker, PdfOverlayField f) in page)
            {
                string text = Resolve(marker, d);
                if (text.Length == 0) continue;

                var font = new XFont(JapaneseFontResolver.FamilyName, Math.Max(6.0, f.フォントPt));
                const double 内側余白Mm = 0.6;
                var rect = new XRect(
                    XUnit.FromMillimeter(f.XMm + 内側余白Mm).Point,
                    XUnit.FromMillimeter(f.YMm).Point,
                    XUnit.FromMillimeter(Math.Max(f.幅Mm - 内側余白Mm * 2, 2)).Point,
                    XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);

                // 個人番号・個人番号又は法人番号は1桁ずつ枠内に描くため、各桁を枠の中央に配置する。
                bool 桁マーカー = 受給者番号桁.IsMatch(marker) || 支払者番号桁.IsMatch(marker);
                PdfTextAlign 揃え = 桁マーカー ? PdfTextAlign.Center : f.揃え;
                gfx.DrawString(text, font, XBrushes.Black, rect, Format(揃え));
            }
        }

        using var outMs = new MemoryStream();
        doc.Save(outMs);
        return outMs.ToArray();
    }

    /// <summary>マーカー文字列を描画値へ解決する（空文字＝描画しない）。現行の代入内容に一致。</summary>
    private static string Resolve(string marker, RetirementWithholdingReportData d)
    {
        switch (marker)
        {
            case "**支給年": return Wareki.年数(d.支給年);
            case "**支払を受ける者_住所又は居所": return d.住所;
            case "**支払を受ける者_1月1日の住所": return d.年始住所;
            case "**支払を受ける者_氏名": return d.氏名;
            case "**退職所得控除額": return Money(d.退職所得控除額);
            case "**勤続年数": return d.勤続年数;
            case "**就職年月日": return WarekiDate(d.就職年月日);
            case "**退職年月日": return WarekiDate(d.退職年月日);
            case "**摘要": return d.摘要;
            case "**支払者_住所(居所)又は所在地": return d.会社所在地;
            case "**支払者_氏名又は名称": return d.会社名;
            case "**支払者_電話": return d.会社電話;
        }

        // 支払を受ける者：個人番号（12桁のときのみ各桁。_12桁→先頭）
        Match r = 受給者番号桁.Match(marker);
        if (r.Success)
        {
            if (d.個人番号.Length != 12) return "";
            int k = int.Parse(r.Groups[1].Value);           // 12..1
            return d.個人番号.Substring(12 - k, 1);
        }

        // 支払者：個人番号又は法人番号（番号区分で13桁／12桁を切替）
        Match p = 支払者番号桁.Match(marker);
        if (p.Success)
        {
            int k = int.Parse(p.Groups[1].Value);           // 13..1
            string num = d.事業所マイナンバー;
            if (d.番号区分 == "1")
            {
                if (num.Length != 13) return "";
                return num.Substring(13 - k, 1);            // _13桁→先頭
            }
            // 番号区分が"1"以外＝法人番号12桁。_13桁は空欄、_12桁→先頭。
            if (num.Length != 12) return "";
            if (k == 13) return "";
            return num.Substring(12 - k, 1);
        }

        // 区分別（所得税法201条 第1項第1号／第1項第2号／第3項）の金額
        int idx =
            marker.StartsWith("**所得税法第201条第１項第１号_") ? 0 :
            marker.StartsWith("**所得税法第201条第１項第２号_") ? 1 :
            marker.StartsWith("**所得税法第201条第３項_") ? 2 : -1;
        if (idx >= 0)
        {
            if (marker.EndsWith("_支払金額")) return Money(d.支払金額[idx]);
            if (marker.EndsWith("_源泉徴収額")) return Money(d.源泉徴収税額[idx]);
            if (marker.EndsWith("_市町村民税")) return Money(d.市町村民税[idx]);
            if (marker.EndsWith("_道府県民税")) return Money(d.道府県民税[idx]);
        }

        return "";
    }

    // 金額列は Excel numFmt 38（#,##0）。現行はDB値をそのまま代入するため 0 も "0" と表示する。
    private static string Money(int v) => v.ToString("#,##0", CultureInfo.InvariantCulture);

    // 日付列は Excel numFmt 58（[$-411]ggge"年"m"月"d"日"）＝和暦（例：令和6年4月1日）。空・不正は空欄。
    private static readonly CultureInfo JaWareki = CreateWarekiCulture();
    private static CultureInfo CreateWarekiCulture()
    {
        var ci = (CultureInfo)CultureInfo.GetCultureInfo("ja-JP").Clone();
        ci.DateTimeFormat.Calendar = new JapaneseCalendar();
        return ci;
    }
    private static string WarekiDate(string ymd)
    {
        if (string.IsNullOrWhiteSpace(ymd)) return "";
        if (!DateTime.TryParse(ymd, out DateTime dt)) return "";
        return dt.ToString("ggy'年'M'月'd'日'", JaWareki);
    }

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
