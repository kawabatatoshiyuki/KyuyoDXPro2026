using System.Text.RegularExpressions;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 法定調書合計表を、背景PDF（官製様式）＋座標レイアウトに集計値を重ねて生成する（方式①）。
/// 実行時は純.NET（PdfSharp＋OS同梱フォント）のみで、外部プロセスや顧客導入物に依存しない。
/// 背景PDFと座標レイアウトはいずれもxlsxマスターに由来するため、様式（罫線・ラベル）は背景で、
/// 値の配置はxlsx編集に追従する。
/// </summary>
public sealed class LegalRecordSummaryPdfGenerator
{
    /// <summary>住所（居所）又は所在地の左余白（mm）。抽出セル左と背景仕切り罫線のズレ(約1.6mm)＋0.5文字分を含む。</summary>
    private const double 住所左余白Mm = 3.3;

    private static readonly Regex 個人番号桁Re = new(@"^\*\*提出者_個人番号又は法人番号_(\d{1,2})番号$", RegexOptions.Compiled);

    /// <summary>右枠外の縦書き注記「【平成28年1月1日以後提出用】」の列中心X（mm）。同列の(注)文字中心の実測に一致。</summary>
    private const double 括弧列中央X_Mm = 192.6;
    /// <summary>同注記の先頭コマ（︻）の中心Y（mm・上詰め起点）。</summary>
    private const double 括弧上端Y_Mm = 22.5;
    /// <summary>同注記の1コマあたりの縦ピッチ（mm・均一行間）。</summary>
    private const double 括弧ピッチMm = 3.25;
    /// <summary>同注記のフォントサイズ（pt）。</summary>
    private const double 括弧フォントPt = 6.5;

    /// <summary>
    /// 個人番号又は法人番号（13桁）の各点線枠の中央X（mm）。背景の縦点線分割位置（14本）を実測し、
    /// 隣り合う分割線の中点として算出した。抽出セル幅が不均一で背景枠と一致しないため、各桁をここへ絶対配置する。
    /// </summary>
    private static readonly double[] 個人番号枠中央Mm =
    {
        34.49, 38.55, 42.59, 46.47, 50.37, 54.27, 58.37,
        62.47, 66.37, 71.22, 76.27, 80.70, 85.13,
    };

    /// <param name="backgroundPdf">官製様式の背景PDF（xlsxのプレースホルダを消して印刷したもの）。</param>
    /// <param name="layout"><see cref="XlsxOverlayLayoutExtractor"/> がxlsxから抽出した配置（マーカー→座標）。</param>
    public byte[] Generate(
        Stream backgroundPdf,
        IReadOnlyDictionary<string, PdfOverlayField> layout,
        LegalRecordSummary summary,
        法定調書提出者情報 提出者)
    {
        JapaneseFontResolver.EnsureRegistered();
        string 番号13 = LegalRecordSummaryXlsxGenerator.Build個人番号13(提出者);

        using var src = new MemoryStream();
        backgroundPdf.CopyTo(src);
        src.Position = 0;
        using PdfDocument doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);

        foreach (var page in layout.GroupBy(kv => kv.Value.ページ))
        {
            if (page.Key < 0 || page.Key >= doc.Pages.Count) continue;
            using XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[page.Key]);

            foreach ((string marker, PdfOverlayField f) in page.Select(x => (x.Key, x.Value)))
            {
                if (!LegalRecordSummaryXlsxGenerator.TryResolve(marker, summary, 提出者, 番号13, out object? v) || v is null)
                    continue;
                string text = Format(v);
                if (text.Length == 0) continue;

                // 個人番号又は法人番号（13桁）：各桁は独立した点線枠。抽出セル幅が背景の（不均一な）点線枠と
                // 一致せず、抽出中心へ置くと桁がドリフトする（前方＝左寄り／後方＝右寄り）。背景の点線分割位置を
                // 実測した枠中央へ、各桁を絶対配置する。
                Match 桁 = 個人番号桁Re.Match(marker);
                if (桁.Success)
                {
                    int n = int.Parse(桁.Groups[1].Value);
                    if (n >= 1 && n <= 個人番号枠中央Mm.Length)
                    {
                        double cx = 個人番号枠中央Mm[n - 1];
                        const double 桁枠幅Mm = 4.0; // Center揃えのため中心が枠中央に来ればよい（幅自体は非依存）
                        var r = new XRect(
                            XUnit.FromMillimeter(cx - 桁枠幅Mm / 2).Point,
                            XUnit.FromMillimeter(f.YMm).Point,
                            XUnit.FromMillimeter(桁枠幅Mm).Point,
                            XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);
                        var 桁font = new XFont(JapaneseFontResolver.FamilyName, f.フォントPt);
                        gfx.DrawString(text, 桁font, XBrushes.Black, r, Format(PdfTextAlign.Center));
                        continue;
                    }
                }

                const double 内側余白Mm = 0.6;
                // テンプレのセルは「**…」テキストプレースホルダのため既定が左揃えになる。実際の値は数値なので
                // 官製様式に合わせ、金額＝右揃え（右罫線から1文字あける）／人員＝中央に統一する（現行 Excel の数値既定に一致）。
                double em = f.フォントPt * 25.4 / 72.0;
                (PdfTextAlign align, double 右余白Mm) = ResolveAlign(marker, f.揃え, em);
                // 左揃えの文字列（住所・氏名・カナ等）はセル左罫線に密着しないよう左余白を広めに取る。
                double 左余白Mm = align == PdfTextAlign.Left ? 1.8 : 内側余白Mm;
                // 提出者ブロックの左寄せ項目（住所・氏名・フリガナ・代表者氏名・代表者フリガナ）は、
                // 開始位置を住所とそろえる。抽出セル左が背景の仕切り罫線より約1.6mm左にあるため、
                // 左揃え＋0.5文字（em）分あけた住所左余白で統一する（氏名系は既定が中央のものもあるため左揃えへ上書き）。
                if (Is提出者左寄せ(marker))
                {
                    align = PdfTextAlign.Left;
                    左余白Mm = 住所左余白Mm;
                }

                var rect = new XRect(
                    XUnit.FromMillimeter(f.XMm + 左余白Mm).Point,
                    XUnit.FromMillimeter(f.YMm).Point,
                    XUnit.FromMillimeter(Math.Max(f.幅Mm - 左余白Mm - 内側余白Mm - 右余白Mm, 2)).Point,
                    XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);

                XFont font;
                if (marker == "**提出者_住所(居所)又は所在地_電話番号")
                    // 電話番号は抽出セルが極端に狭く（約1mm）、fit-to-width だと下限4ptまで縮んで
                    // 背景ラベル「電話」より小さくなる。抽出フォントサイズ（＝ラベルと同等の約7.7pt）の
                    // まま、幅縮小をかけずに描画する（住所セルにはヨコ方向の余裕がある）。
                    font = new XFont(JapaneseFontResolver.FamilyName, f.フォントPt);
                else
                {
                    // 数値データ（金額・人員）はセルごとの抽出フォントに 10.3pt/9.44pt のばらつきがあるため、
                    // 統一サイズを基準にして金額と人員のフォント・サイズをそろえる。長い値が枠幅を超える場合のみ
                    // fit-to-width で縮小する（住所・氏名などは従来どおり抽出フォント基準）。
                    double baseFontPt = Is数値データ(marker) ? 数値統一フォントPt : f.フォントPt;
                    font = FitFont(gfx, text, Math.Max(6.0, baseFontPt), rect.Width);
                }
                gfx.DrawString(text, font, XBrushes.Black, rect, Format(align));
            }
        }

        DrawBracketNote(doc);

        using var outMs = new MemoryStream();
        doc.Save(outMs);
        return outMs.ToArray();
    }

    /// <summary>
    /// 右枠外の縦書き注記「【平成28年1月1日以後提出用】」を均一な行間・上詰めで描画する。
    /// テンプレxlsxではこの文言がフレーム行（行高不揃い）にまたがる4セルに分割配置され、行間が不揃い＋
    /// 末尾「】」が下端で切れていた。行高はフレームと共有で変更不可のため、背景生成時に当該4セルを消去し
    /// （clear_lr.ps1）、ここで一定ピッチ・上詰めで描き直す（H.pdf 準拠）。「28」は縦中横（横並び1コマ）。
    /// </summary>
    private static void DrawBracketNote(PdfDocument doc)
    {
        if (doc.Pages.Count == 0) return;
        using XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[0]);

        // 各コマの文字（上→下）。「︻」「︼」は【】の縦書き字形（縦組み用presentation form）。
        string[] slots = { "︻", "平", "成", "28", "年", "1", "月", "1", "日", "以", "後", "提", "出", "用", "︼" };
        var font = new XFont(JapaneseFontResolver.FamilyName, 括弧フォントPt);
        var fontSmall = new XFont(JapaneseFontResolver.FamilyName, 括弧フォントPt * 0.60); // 「28」縦中横用
        var center = new XStringFormat { Alignment = XStringAlignment.Center, LineAlignment = XLineAlignment.Center };

        for (int i = 0; i < slots.Length; i++)
        {
            double cy = 括弧上端Y_Mm + i * 括弧ピッチMm;
            XFont f = slots[i] == "28" ? fontSmall : font;
            var rect = new XRect(
                XUnit.FromMillimeter(括弧列中央X_Mm - 3.0).Point,
                XUnit.FromMillimeter(cy - 括弧ピッチMm / 2.0).Point,
                XUnit.FromMillimeter(6.0).Point,
                XUnit.FromMillimeter(括弧ピッチMm).Point);
            gfx.DrawString(slots[i], f, XBrushes.Black, rect, center);
        }
    }

    /// <summary>
    /// 文字列が指定幅（pt）に収まるよう、必要な分だけフォントを縮小して返す（fit-to-width）。
    /// 収まるなら基準サイズのまま。下限 <paramref name="floorPt"/> までで打ち切る（極端に長い場合は多少はみ出す）。
    /// </summary>
    private static XFont FitFont(XGraphics gfx, string text, double sizePt, double maxWidthPt, double floorPt = 4.0)
    {
        var font = new XFont(JapaneseFontResolver.FamilyName, sizePt);
        while (sizePt > floorPt && gfx.MeasureString(text, font).Width > maxWidthPt)
        {
            sizePt -= 0.5;
            font = new XFont(JapaneseFontResolver.FamilyName, sizePt);
        }
        return font;
    }

    /// <summary>数値データ（金額・人員）の統一フォントサイズ（pt）。セル毎の抽出フォントのばらつきを吸収する。</summary>
    private const double 数値統一フォントPt = 10.3;

    /// <summary>金額（支払金額・源泉徴収税額）または人員の数値データ項目か。フォントサイズ統一の対象。</summary>
    private static bool Is数値データ(string marker) =>
        marker.EndsWith("_支払金額", StringComparison.Ordinal) ||
        marker.EndsWith("_源泉徴収税額", StringComparison.Ordinal) ||
        marker.EndsWith("_人員", StringComparison.Ordinal) ||
        marker.EndsWith("_人員_個人", StringComparison.Ordinal) ||
        marker.EndsWith("_人員_個人以外", StringComparison.Ordinal) ||
        marker.EndsWith("_源泉徴収税額のない者", StringComparison.Ordinal);

    /// <summary>提出者ブロックで開始位置を住所とそろえる（左揃え）項目か。</summary>
    private static bool Is提出者左寄せ(string marker) => marker is
        "**提出者_住所(居所)又は所在地" or
        "**提出者_氏名又は名称" or
        "**提出者_フリガナ_氏名又は名称" or
        "**提出者_代表者氏名" or
        "**提出者_フリガナ_代表者氏名";

    /// <summary>
    /// マーカー種別ごとの揃えを決める。金額（支払金額・源泉徴収税額）と人員（総数・個人・個人以外・源泉徴収税額のない者）は
    /// いずれも右揃え（右罫線から1文字＝em分あける・金額と同じ余白）で統一する。それ以外（提出者情報・番号桁など）はテンプレ既定を維持。
    /// </summary>
    private static (PdfTextAlign align, double 右余白Mm) ResolveAlign(string marker, PdfTextAlign 既定, double em)
    {
        if (marker.EndsWith("_支払金額", StringComparison.Ordinal) ||
            marker.EndsWith("_源泉徴収税額", StringComparison.Ordinal))
            return (PdfTextAlign.Right, em);
        if (marker.EndsWith("_人員", StringComparison.Ordinal) ||
            marker.EndsWith("_人員_個人", StringComparison.Ordinal) ||
            marker.EndsWith("_人員_個人以外", StringComparison.Ordinal) ||
            marker.EndsWith("_源泉徴収税額のない者", StringComparison.Ordinal))
            return (PdfTextAlign.Right, em);
        return (既定, 0);
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

    private static string Format(object v) => v switch
    {
        long l => l.ToString("#,0"),
        int i => i.ToString(),
        string s => s,
        _ => v.ToString() ?? "",
    };
}
