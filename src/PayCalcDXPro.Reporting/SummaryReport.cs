using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 年末調整 集計表（納付書用）（現行 R_集計表）のPDFを生成する高レベルAPI（方式①＝背景様式オーバーレイ）。
/// A4縦1面。上部「納付書の記載例」＝集計値（俸給給与／賞与／不足税額／超過税額／本税 × 人員・支給額・税額）と、
/// 下部「年末調整額明細」＝従業員別（整理番号・氏名・調整額）を左25行＋右25行で並べる。
///
/// 座標較正：背景PDF（Excel印刷）はマーカー抽出座標に対し横がスケール＋余白でずれるため、背景罫線を実測した
/// アフィン変換で写す（横 bg=13.34+0.8712×X／縦 bg=-0.181+1.016×Y）。明細行は実測ピッチ7.286mmで積む。
/// </summary>
public sealed class SummaryReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.SummaryTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.SummaryBackground.pdf";

    private const double 内側余白Mm = 0.4;

    // 右寄せ数値・調整額は右罫線から1文字分（1em）あける。氏名は0.5文字分右へシフト（現行の見た目に合わせる）。
    private const double 数値右余白Em = 1.0;
    private const double 調整額右余白Em = 1.0;
    private const double 氏名右シフトEm = 0.5;

    // 背景罫線実測によるアフィン変換（抽出座標系→背景様式）。
    private const double 横補正切片Mm = 13.34;
    private const double 横補正係数 = 0.8712;
    private const double 縦補正切片Mm = -0.181;
    private const double 縦補正係数 = 1.016;
    // フォントは横スケールに合わせて詰める（背景の狭いセルに収める）。目視で調整可。
    private const double フォント係数 = 0.8712;

    // 明細（年末調整額明細）：実測グリッド。左右各25行。行1の枠上端＝抽出ベースをアフィン変換した値、
    // 行ピッチ＝背景実測7.286mm（25行）。
    private const double 明細行ピッチMm = 7.286;
    private const int 明細行数 = 25;

    // 明細の各列マーカー（左／右）。氏名は全角スペース2つに注意。
    private const string 左整理番号 = "**年末調整額明細（左）_整理番号";
    private const string 左氏名 = "**年末調整額明細（左）_氏  名";
    private const string 左調整額 = "**年末調整額明細（左）_調整額";
    private const string 右整理番号 = "**年末調整額明細（右）_整理番号";
    private const string 右氏名 = "**年末調整額明細（右）_氏  名";
    private const string 右調整額 = "**年末調整額明細（右）_調整額";

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>抽出されたマーカー一覧（ビルダーが参照用）。</summary>
    public static IReadOnlyList<string> Markers =>
        CachedLayout.Value.Select(x => x.marker).Distinct().ToArray();

    /// <summary>明細1件（従業員1名分）。調整額は年末調整済かつ値ありのときのみ非null。</summary>
    public readonly record struct Detail(string 整理番号, string 氏名, string? 調整額);

    /// <summary>
    /// 集計値（marker→表示文字列）と明細一覧から集計表PDF（1ページ）を生成する。
    /// 明細は先頭25名を左列・次の25名を右列に並べる（現行と同じ＝最大50名）。
    /// </summary>
    public byte[] CreatePdf(IReadOnlyDictionary<string, string?> summaryValues, IReadOnlyList<Detail> details)
    {
        JapaneseFontResolver.EnsureRegistered();

        using Stream background = OpenResource(BackgroundResource);
        using var src = new MemoryStream();
        background.CopyTo(src);
        src.Position = 0;
        using PdfDocument doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
        if (doc.Pages.Count == 0) { using var e = new MemoryStream(); doc.Save(e); return e.ToArray(); }

        using XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[0]);

        var layout = CachedLayout.Value;
        var byMarker = layout.GroupBy(x => x.marker).ToDictionary(g => g.Key, g => g.First().field);

        // ① 上部集計値（明細以外の単一マーカー）を描画。
        foreach ((string marker, PdfOverlayField f) in layout)
        {
            if (marker.StartsWith("**年末調整額明細")) continue;
            if (!summaryValues.TryGetValue(marker, out string? text) || string.IsNullOrEmpty(text)) continue;
            if (Is数値項目(marker))
                DrawIn(gfx, f, text, PdfTextAlign.Right, 数値右余白Em * Em(f));
            else
                DrawIn(gfx, f, text);
        }

        // ② 明細（左25＋右25）。各列のベースマーカーをアフィン変換した位置から行ピッチで積む。
        DrawDetails(gfx, byMarker, details);

        using var outMs = new MemoryStream();
        doc.Save(outMs);
        return outMs.ToArray();
    }

    private static void DrawDetails(XGraphics gfx,
        IReadOnlyDictionary<string, PdfOverlayField> byMarker, IReadOnlyList<Detail> details)
    {
        for (int i = 0; i < details.Count; i++)
        {
            bool 左 = i < 明細行数;
            int rowIdx = 左 ? i : i - 明細行数;
            if (rowIdx >= 明細行数) break; // 50名を超えた分は現行同様あふれるため描かない。

            (string mNo, string mName, string mAdj) = 左
                ? (左整理番号, 左氏名, 左調整額)
                : (右整理番号, 右氏名, 右調整額);

            Detail d = details[i];
            DrawDetailCell(gfx, byMarker[mNo], rowIdx, d.整理番号);
            DrawDetailCell(gfx, byMarker[mName], rowIdx, d.氏名, xShiftEm: 氏名右シフトEm);
            if (!string.IsNullOrEmpty(d.調整額))
                DrawDetailCell(gfx, byMarker[mAdj], rowIdx, d.調整額!, align: PdfTextAlign.Right, 右余白Em: 調整額右余白Em);
        }
    }

    private static void DrawDetailCell(XGraphics gfx, PdfOverlayField baseField, int rowIdx, string text,
        PdfTextAlign? align = null, double 右余白Em = 0, double xShiftEm = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        double yTop = baseField.YMm + rowIdx * 明細行ピッチMm;
        var f = baseField with { YMm = yTop, 高Mm = 明細行ピッチMm };
        double em = Em(f);
        DrawIn(gfx, f, text, align, 右余白Em * em, xShiftEm * em);
    }

    private static void DrawIn(XGraphics gfx, PdfOverlayField f, string text,
        PdfTextAlign? align = null, double 右余白Mm = 0, double xShiftMm = 0)
    {
        var font = new XFont(JapaneseFontResolver.FamilyName, Math.Max(4.0, f.フォントPt));
        var rect = new XRect(
            XUnit.FromMillimeter(f.XMm + 内側余白Mm + xShiftMm).Point,
            XUnit.FromMillimeter(f.YMm).Point,
            XUnit.FromMillimeter(Math.Max(f.幅Mm - 内側余白Mm * 2 - 右余白Mm, 2)).Point,
            XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);
        gfx.DrawString(text, font, XBrushes.Black, rect, Format(align ?? f.揃え));
    }

    /// <summary>フォント1文字分（1em）の物理幅mm。pt→mm換算（1pt = 25.4/72 mm）。</summary>
    private static double Em(PdfOverlayField f) => f.フォントPt * 25.4 / 72.0;

    /// <summary>上部集計表の数値セル（人員・支給額・税額）か。年度・種別などの文字は除外。</summary>
    private static bool Is数値項目(string marker) =>
        marker.EndsWith("_人員") || marker.EndsWith("_支給額") || marker.EndsWith("_税額");

    private static IReadOnlyList<(string marker, PdfOverlayField field)> LoadLayout()
    {
        using Stream xlsx = OpenResource(TemplateResource);
        var raw = XlsxOverlayLayoutExtractor.ExtractAll(xlsx);

        var corrected = new List<(string marker, PdfOverlayField field)>(raw.Count);
        foreach ((string marker, PdfOverlayField field) in raw)
        {
            var f = field with
            {
                XMm = 横補正切片Mm + 横補正係数 * field.XMm,
                幅Mm = 横補正係数 * field.幅Mm,
                YMm = 縦補正切片Mm + 縦補正係数 * field.YMm,
                高Mm = 縦補正係数 * field.高Mm,
                フォントPt = フォント係数 * field.フォントPt,
            };
            corrected.Add((marker, f));
        }
        return corrected;
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

    private static Stream OpenResource(string name)
        => typeof(SummaryReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
