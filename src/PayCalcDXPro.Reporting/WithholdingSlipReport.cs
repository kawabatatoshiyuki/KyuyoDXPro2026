using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 給与所得の源泉徴収票（現行 R_源泉徴収票）のPDFを生成する高レベルAPI（方式①＝官製様式オーバーレイ）。
/// この様式は 215 マーカーと複雑な業務ロジックを持つため、マーカー→値の解決は呼び出し側（データ構築層）で行い、
/// 本クラスは「解決済みの値辞書」を背景PDF上へ重ねるだけの汎用オーバーレイに徹する（現行 updateReport の
/// セル代入結果に相当する `marker → 表示文字列` を受け取る）。座標はテンプレ(sheet1「源泉徴収票」)から自動抽出。
/// A4縦1面（1従業員＝1ページ）。較正はまず恒等（抽出座標そのまま）で、視認しながら AdjustField で調整する。
/// </summary>
public sealed class WithholdingSlipReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.WithholdingSlipTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.WithholdingSlipBackground.pdf";

    private const double 内側余白Mm = 0.6;

    // 背景PDF（Excel印刷）はマーカー座標系に対し「スケール＋左上余白」でずれている。
    // 背景の罫線を実測して得た相似変換で、抽出座標を背景の実位置へ写す（2026-07-17）。
    //   横：bg_X = 9.90 + 0.9323 × marker_X（13罫線でフィット）
    //   縦：bg_Y = 4.54 + 0.9365 × marker_Y（3行でフィット。年分が自動的に約1.3文字下がる）
    private const double 横補正切片Mm = 9.90;
    private const double 横補正係数 = 0.9323;
    private const double 縦補正切片Mm = 4.54;
    private const double 縦補正係数 = 0.9365;
    private const double フォント係数 = 0.933;   // スケールに合わせて字面も縮小

    // ユーザー指示の追加縦調整（左右共通）：（摘要）ブロックを境に印字を下へずらす（2026-07-17）。
    //   （摘要）より上部＝0.3文字下／（摘要）より下部＝1文字下。1文字≈2.8mm。
    private const double 文字Mm = 2.8;
    private const double 摘要境界Y = 88.0;        // マーカー座標での境界（摘要ブロック下端付近）
    private const double 上部追加下げMm = 0.3 * 文字Mm;
    private const double 下部追加下げMm = 1.0 * 文字Mm;

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>抽出されたマーカー一覧（データ構築層が「どのマーカーを埋めるべきか」を参照するため）。</summary>
    public static IReadOnlyList<string> Markers =>
        CachedLayout.Value.Select(x => x.marker).Distinct().ToArray();

    /// <summary>
    /// 解決済みの値（marker→表示文字列）を背景様式へ重ねて1従業員分のPDFを生成する。
    /// 値が無い／空文字のマーカーは描画しない。
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

            foreach ((string marker, PdfOverlayField f) in page)
            {
                if (!values.TryGetValue(marker, out string? text) || string.IsNullOrEmpty(text)) continue;

                var font = new XFont(JapaneseFontResolver.FamilyName, Math.Max(4.0, f.フォントPt));
                var rect = new XRect(
                    XUnit.FromMillimeter(f.XMm + 内側余白Mm).Point,
                    XUnit.FromMillimeter(f.YMm).Point,
                    XUnit.FromMillimeter(Math.Max(f.幅Mm - 内側余白Mm * 2, 2)).Point,
                    XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);

                gfx.DrawString(text, font, XBrushes.Black, rect, Format(f.揃え));
            }
        }

        using var outMs = new MemoryStream();
        doc.Save(outMs);
        return outMs.ToArray();
    }

    /// <summary>複数従業員分を1つの複数ページPDF（1従業員＝1ページ）にまとめる。</summary>
    public byte[] CreatePdf(IReadOnlyList<IReadOnlyDictionary<string, string?>> employees)
    {
        if (employees.Count == 0) return Array.Empty<byte>();
        if (employees.Count == 1) return CreatePdf(employees[0]);

        using var outDoc = new PdfDocument();
        foreach (var values in employees)
        {
            byte[] one = CreatePdf(values);
            using var ms = new MemoryStream(one);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            foreach (var page in doc.Pages)
                outDoc.AddPage(page);
        }
        using var outMs = new MemoryStream();
        outDoc.Save(outMs);
        return outMs.ToArray();
    }

    private static IReadOnlyList<(string marker, PdfOverlayField field)> LoadLayout()
    {
        using Stream xlsx = OpenResource(TemplateResource);
        var raw = XlsxOverlayLayoutExtractor.ExtractAll(xlsx);

        var corrected = new List<(string marker, PdfOverlayField field)>(raw.Count);
        foreach ((string marker, PdfOverlayField field) in raw)
        {
            // 抽出座標（フルスケール）→ 背景PDFの実位置（スケール＋余白）へ相似変換。
            // さらに（摘要）を境に上部/下部で追加の下げを加える（ユーザー指示）。
            // ただし個別に中央合わせする欄（生保内訳・住宅内訳・氏名/フリガナ）は AdjustField で位置決めするので一律下げは除外。
            double 追加下げMm = 個別配置(marker) ? 0 : (field.YMm < 摘要境界Y ? 上部追加下げMm : 下部追加下げMm);
            var f = field with
            {
                XMm = 横補正切片Mm + 横補正係数 * field.XMm,
                幅Mm = 横補正係数 * field.幅Mm,
                YMm = 縦補正切片Mm + 縦補正係数 * field.YMm + 追加下げMm,
                高Mm = 縦補正係数 * field.高Mm,
                フォントPt = フォント係数 * field.フォントPt,
            };
            corrected.Add((marker, AdjustField(marker, f)));
        }
        return corrected;
    }

    /// <summary>生保内訳・住宅内訳・氏名/フリガナは AdjustField で個別に位置決めする（一律の下げを除外）。</summary>
    private static bool 個別配置(string marker)
        => marker.Contains("生命保険料の金額の内訳")
        || marker.Contains("住宅借入金等特別控除の額の内訳")
        || 氏名フリガナか(marker);

    private static bool 氏名フリガナか(string marker)
        => (marker.Contains("控除対象配偶者_") || marker.Contains("控除対象扶養親族_") || marker.Contains("１６歳未満の扶養親族_"))
           && (marker.EndsWith("_氏名") || marker.Contains("（フリガナ）"));

    // 氏名／フリガナ セルの上下中央Y（背景罫線の実測。扶養/16歳未満は同一行＝N番目、行送り11.45mm）。
    private static double 氏名フリガナ中央Y(string marker)
    {
        bool フリガナ = marker.Contains("（フリガナ）");
        if (marker.Contains("控除対象配偶者_"))
            return フリガナ ? 108.83 : 112.26;
        int n = 1;
        for (int k = 1; k <= 4; k++) if (marker.Contains($"_{k}_")) { n = k; break; }
        double 行送り = 11.45 * (n - 1);
        return (フリガナ ? 120.25 : 123.67) + 行送り;
    }

    // 上段金額行（支払金額を含む行）＝支払金額／給与所得控除後／所得控除計／源泉徴収額。
    private static bool 上段金額行(string marker)
        => marker.StartsWith("**支払金額_")
        || marker.StartsWith("**給与所得控除後の金額(調整控除後)_")
        || marker.StartsWith("**所得控除の額の合計額_")
        || marker.StartsWith("**源泉徴収額_");

    private const double 金額拡大 = 1.2;   // 種別・上段金額のフォント拡大率

    /// <summary>項目別のレイアウト微調整（ユーザー指示・背景罫線の実測に基づく）。</summary>
    private static PdfOverlayField AdjustField(string marker, PdfOverlayField f)
    {
        // ⓪ 種別：フォントを少し大きく。
        if (marker == "**種別")
            return f with { フォントPt = f.フォントPt * 金額拡大 };

        // ⓪' 上段金額行：フォント拡大＋桁別の揃え（百万=右／千=中央／円=左）。
        if (上段金額行(marker))
        {
            var 桁揃え = marker.EndsWith("_円") ? PdfTextAlign.Left
                       : marker.EndsWith("_千") ? PdfTextAlign.Center
                       : PdfTextAlign.Right;   // _内（百万の位）
            return f with { フォントPt = f.フォントPt * 金額拡大, 揃え = 桁揃え };
        }

        // ① 生命保険料の金額の内訳／住宅借入金等特別控除の額の内訳：セル中心＋0.5文字下（背景罫線の実測セル中心＝生保94.67／住宅1回目100.15／住宅2回目105.17mm）。
        double? 中央Y = null;
        if (marker.Contains("生命保険料の金額の内訳"))
            中央Y = 94.67 + 0.5 * 文字Mm;
        else if (marker.Contains("住宅借入金等特別控除の額の内訳"))
            中央Y = ((marker.Contains("(2回目)") || marker.Contains("（２回目）") || marker.Contains("可能額")) ? 105.17 : 100.15) + 0.5 * 文字Mm;
        if (中央Y is double cy)
            return f with { YMm = cy - f.高Mm / 2 };

        // ② 控除対象配偶者・控除対象扶養親族・16歳未満扶養親族の 氏名／フリガナ：背景罫線実測のセル中心へ上下中央＋フリガナは少し小さく。
        if (氏名フリガナか(marker))
        {
            if (marker.Contains("（フリガナ）"))
                f = f with { フォントPt = f.フォントPt * 0.82 };
            return f with { YMm = 氏名フリガナ中央Y(marker) - f.高Mm / 2 };
        }

        // ③ 国民年金保険料等／基礎控除／旧長期損害保険料／所得金額調整控除額：右詰め（カンマ編集はビルダー側）。
        if (marker.EndsWith("_国民年金保険料等の金額_円")
            || marker.EndsWith("_基礎控除の額")
            || marker.EndsWith("_旧長期損害保険料の金額_円")
            || marker.EndsWith("_所得金額調整控除額"))
            return f with { 揃え = PdfTextAlign.Right };

        // ④ 特定親族特別控除の額の「千」枠：右寄せ。
        if (marker == "**特定親族特別控除額_千")
            return f with { 揃え = PdfTextAlign.Right };

        return f;
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
        => typeof(WithholdingSlipReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
