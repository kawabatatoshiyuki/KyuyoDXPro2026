using System.Text;
using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 給与支払報告書（総括表）（現行 R_総括表）のPDFを生成する高レベルAPI（方式①＝官製様式オーバーレイ）。
/// A4横1面・4面付け（提出先を最大4件、各象限に1件ずつ）。会社（提出者）情報は全4象限に同じ値を重ねる
/// （テンプレxlsxでは同一マーカーが4セルに現れるため、ExtractAll で重複を保持して各位置へ描画する）。
/// 提出先ごとの市町村名・支払期間・報告人数・退職者人員は「_1〜_4」のサフィックスで各象限に対応する。
/// 本クラスは「解決済みの値辞書（marker→表示文字列）」を背景様式へ重ねるだけの汎用オーバーレイに徹する
/// （現行 createReport のセル代入結果に相当）。座標はテンプレから自動抽出し、較正は当面恒等から視認調整する。
/// </summary>
public sealed class PayReportSummaryReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.PayReportSummaryTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.PayReportSummaryBackground.pdf";

    private const double 内側余白Mm = 0.6;

    // 背景PDF（Excel印刷）と抽出座標のズレを写す相似変換。この4面付け様式は横方向で抽出座標が
    // 背景よりわずかに広く（左ほど左へずれる）ため、背景の縦罫線（個人番号13マスの分割線）を実測して
    // 横アフィンを較正した（2026-08-10）。左右両象限とも box1/box7/box13 の分割線に一致：
    //   横：bg_X = 7.20 + 0.9429 × marker_X
    //   縦：おおむね恒等（行位置は背景と一致）。フォントは横係数に合わせて縮小。
    private const double 横補正切片Mm = 7.20;
    private const double 横補正係数 = 0.9429;
    private const double 縦補正切片Mm = 0.0;
    private const double 縦補正係数 = 1.0;
    private const double フォント係数 = 0.9429;

    // 4面付けの下段象限は、背景の象限オフセット（実測103.0mm）に対し抽出座標が103.88mmと約0.88mm大きく、
    // 下段全体が背景より下にずれる（所在地フリガナ以降のずれ・支払期間の下罫線かぶりの原因）。
    // 下段（抽出Y≥象限境界）を象限ズレ分だけ引き上げ、上段と行位置を揃える（2026-08-17）。
    private const double 象限境界YMm = 103.0;
    private const double 象限ズレMm = 0.88;

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>抽出されたマーカー一覧（データ構築層が「どのマーカーを埋めるべきか」を参照するため）。</summary>
    public static IReadOnlyList<string> Markers =>
        CachedLayout.Value.Select(x => x.marker).Distinct().ToArray();

    /// <summary>
    /// 解決済みの値（marker→表示文字列）を背景様式へ重ねて総括表1ページ分のPDFを生成する。
    /// 値が無い／空文字のマーカーは描画しない。会社情報マーカーは4象限すべてに同一値が重なる。
    /// </summary>
    public byte[] CreatePdf(IReadOnlyDictionary<string, string?> values)
    {
        JapaneseFontResolver.EnsureRegistered();

        using Stream background = OpenResource(BackgroundResource);
        using var src = new MemoryStream();
        background.CopyTo(src);
        src.Position = 0;
        using PdfDocument doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);

        foreach (var page in CachedLayout.Value.GroupBy(x => x.field.ページ))
        {
            if (page.Key < 0 || page.Key >= doc.Pages.Count) continue;
            using XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[page.Key]);

            // フリガナ行の抽出セルは対応する値セルより右に広く、そのまま縮小基準にすると枠からはみ出す
            // （所在地カナが切れる）。フリガナは「（フリガナ）」を除いたマーカー＝値セルの幅に合わせて収める。
            var 値幅Mm = page.GroupBy(x => x.marker).ToDictionary(g => g.Key, g => g.First().field.幅Mm);

            foreach ((string marker, PdfOverlayField f) in page)
            {
                if (!values.TryGetValue(marker, out string? text) || string.IsNullOrEmpty(text)) continue;

                var rect = new XRect(
                    XUnit.FromMillimeter(f.XMm + 内側余白Mm).Point,
                    XUnit.FromMillimeter(f.YMm).Point,
                    XUnit.FromMillimeter(Math.Max(f.幅Mm - 内側余白Mm * 2, 2)).Point,
                    XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);

                if (フリガナか(marker))
                {
                    // 幅は対応する値セル（（フリガナ）を除いたマーカー）に合わせて枠内に収める。
                    string baseMarker = marker.Replace("（フリガナ）", "");
                    double 幅Mm = 値幅Mm.TryGetValue(baseMarker, out var w) ? w : f.幅Mm;
                    double maxWidthPt = XUnit.FromMillimeter(Math.Max(幅Mm - 内側余白Mm * 2, 2)).Point;
                    var frect = new XRect(rect.X, rect.Y, maxWidthPt, rect.Height);
                    // フリガナは1行で幅に合わせて縮小（はみ出し対策）。
                    var font = FitFontToWidth(gfx, text, f.フォントPt, maxWidthPt);
                    gfx.DrawString(text, font, XBrushes.Black, frect, Format(f.揃え));
                }
                else if (折返し対象(marker))
                {
                    // 氏名・名称・所在地などは枠内に入りきらない場合は改行して全体表示（必要なら高さに合わせて縮小）。
                    DrawWrappedFit(gfx, text, f.フォントPt, rect, f.揃え);
                }
                else
                {
                    var font = new XFont(JapaneseFontResolver.FamilyName, Math.Max(4.0, f.フォントPt));
                    gfx.DrawString(text, font, XBrushes.Black, rect, Format(f.揃え));
                }
            }
        }

        using var outMs = new MemoryStream();
        doc.Save(outMs);
        return outMs.ToArray();
    }

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
                YMm = 縦補正切片Mm + 縦補正係数 * field.YMm - (field.YMm >= 象限境界YMm ? 象限ズレMm : 0.0),
                高Mm = 縦補正係数 * field.高Mm,
                フォントPt = フォント係数 * field.フォントPt,
            };
            corrected.Add((marker, AdjustField(marker, f)));
        }
        return corrected;
    }

    // 給与の支払期間（令和年・月分から・月分まで）の行中央Y（mm・背景罫線の実測）。上段象限＝TL/TR、下段象限＝BL/BR。
    // 抽出セル上端が背景の行より少し上にあり、既定の縦中央だと上罫線にかぶるため、実測した行中央へ配置する。
    private const double 支払期間行中央Y上Mm = 22.8;   // 背景罫線 21.3〜24.3 の中央
    private const double 支払期間行中央Y下Mm = 125.80;  // 上段22.8＋背景象限差103.0（旧126.68は象限ズレ分だけ下すぎ＝下罫線かぶり）

    // 給与支払者の氏名又は名称フリガナ：4象限で上罫線からの距離を揃える絶対配置（背景罫線の実測 2026-08-17）。
    // 象限オフセットが背景(103.0mm)とコード(103.88mm)で約0.88mmずれるため、下げ量でなく背景罫線基準で配置する。
    // Format は LineAlignment=Center ＝文字中央が枠中央に来るので、枠中央Yを「上罫線＋距離」に合わせる。
    private const double フリガナ行上罫線Y上Mm = 30.5;   // 上段：個人番号行とフリガナ行の境界（実線）
    private const double フリガナ行上罫線Y下Mm = 133.5;  // 下段：同上
    private const double フリガナ文字_上罫線からの距離Mm = 1.5;  // 上罫線〜点線(約3mm)の中央へ文字中央を置く

    private const string 氏名フリガナMarker = "**給与支払者の氏名又は名称（フリガナ）";

    /// <summary>項目別のレイアウト微調整（ユーザー指示・背景罫線の実測に基づく）。</summary>
    private static PdfOverlayField AdjustField(string marker, PdfOverlayField f)
    {
        // 給与の支払期間の値：枠内で縦中央へ（上罫線かぶり対策）。
        if (marker.StartsWith("**給与の支払期間", StringComparison.Ordinal))
        {
            double 行中央 = f.YMm < 60 ? 支払期間行中央Y上Mm : 支払期間行中央Y下Mm;
            return f with { YMm = 行中央 - f.高Mm / 2.0 };
        }
        // 給与支払者の氏名又は名称フリガナ：既定フォントを1pt小さく。4象限とも上罫線から同じ距離へ絶対配置。
        if (marker == 氏名フリガナMarker)
        {
            double 上罫線 = f.YMm < 60 ? フリガナ行上罫線Y上Mm : フリガナ行上罫線Y下Mm;
            double 中央 = 上罫線 + フリガナ文字_上罫線からの距離Mm;
            return f with { フォントPt = Math.Max(4.0, f.フォントPt - 1.0), YMm = 中央 - f.高Mm / 2.0 };
        }
        return f;
    }

    /// <summary>フリガナ（会社名カナ・所在地カナ）のマーカーか。1行で幅に合わせて縮小表示する対象。</summary>
    private static bool フリガナか(string marker) => marker.Contains("（フリガナ）");

    /// <summary>氏名・名称・所在地など、入りきらない場合に改行（＋必要なら縮小）して全体表示する対象か。</summary>
    private static bool 折返し対象(string marker) => !フリガナか(marker) && marker is
        "**給与支払者の氏名又は名称" or
        "**同上の所在地" or
        "**所得税の源泉徴収をしている事務所又は事業の名称" or
        "**給与支払者が法人である場合の代表者の氏名" or
        "**連絡者の氏名、所属課、係名及び電話番号_連絡者名";

    /// <summary>指定幅（pt）に1行で収まるようフォントを縮小して返す（fit-to-width・下限4pt）。</summary>
    private static XFont FitFontToWidth(XGraphics gfx, string text, double basePt, double maxWidthPt, double floorPt = 4.0)
    {
        double pt = Math.Max(floorPt, basePt);
        var font = new XFont(JapaneseFontResolver.FamilyName, pt);
        while (pt > floorPt && gfx.MeasureString(text, font).Width > maxWidthPt)
        {
            pt -= 0.5;
            font = new XFont(JapaneseFontResolver.FamilyName, pt);
        }
        return font;
    }

    /// <summary>
    /// 文字列を枠幅で折り返し、枠高に収まるようフォントを縮小しつつ、枠内で上下中央に描画する（日本語は文字単位で折返し）。
    /// 枠内に全体を表示するための処理（改行で入りきらない分は縮小でカバー・下限4pt）。
    /// </summary>
    private static void DrawWrappedFit(XGraphics gfx, string text, double basePt, XRect rect, PdfTextAlign align)
    {
        double pt = Math.Max(4.0, basePt);
        XFont font;
        List<string> lines;
        while (true)
        {
            font = new XFont(JapaneseFontResolver.FamilyName, pt);
            lines = WrapByWidth(gfx, text, font, rect.Width);
            double totalH = font.GetHeight() * lines.Count;
            if (totalH <= rect.Height || pt <= 4.0) break;
            pt -= 0.5;
        }

        double lineH = font.GetHeight();
        double top = rect.Y + Math.Max(0, (rect.Height - lineH * lines.Count) / 2.0);
        var fmt = new XStringFormat { Alignment = Format(align).Alignment, LineAlignment = XLineAlignment.Near };
        for (int i = 0; i < lines.Count; i++)
        {
            var lr = new XRect(rect.X, top + i * lineH, rect.Width, lineH);
            gfx.DrawString(lines[i], font, XBrushes.Black, lr, fmt);
        }
    }

    /// <summary>日本語文字列を、各行が maxWidth（pt）に収まるよう文字単位で折り返す。</summary>
    private static List<string> WrapByWidth(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (char ch in text)
        {
            if (cur.Length > 0 && gfx.MeasureString(cur.ToString() + ch, font).Width > maxWidth)
            {
                lines.Add(cur.ToString());
                cur.Clear();
            }
            cur.Append(ch);
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add("");
        return lines;
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
        => typeof(PayReportSummaryReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
