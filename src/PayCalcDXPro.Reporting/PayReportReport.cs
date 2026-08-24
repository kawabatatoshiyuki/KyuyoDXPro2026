using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 給与支払報告書（個人別明細書）（現行 R_給与支払報告書）のPDFを生成する（方式①＝官製様式オーバーレイ）。
/// マーカー→値の解決は源泉徴収票と 78/80 共通のため <see cref="PayCalcDXPro.Web"/> 側のビルダーを共用し、
/// 本クラスは「解決済みの値辞書」を給与支払報告書の背景PDFへ重ねる汎用オーバーレイに徹する。A4横2面。
/// 較正（背景のスケール＋余白の相似変換）は給報テンプレ独自に持つ。
/// </summary>
public sealed class PayReportReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.PayReportTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.PayReportBackground.pdf";

    private const double 内側余白Mm = 0.6;

    // 背景PDF（Excel印刷）はマーカー座標系に対しスケール＋左上余白でずれる。給報背景の罫線を実測して得た相似変換で写す。
    //   横：bg_X = 4.65 + 0.9675 × marker_X（金額行の縦罫線4本でフィット）
    //   縦：bg_Y = 2.33 + 0.9701 × marker_Y（金額行・支払者番号行でフィット）
    private const double 横補正切片Mm = 4.65;
    private const double 横補正係数 = 0.9675;
    private const double 縦補正切片Mm = 2.33;
    private const double 縦補正係数 = 0.9701;
    private const double フォント係数 = 0.933;   // 源泉徴収票と同じ（給報の抽出ptもほぼ同一なので同サイズになる）
    private const double 金額拡大 = 1.2;         // 種別・上段金額の拡大（源泉と同じ）

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    public static IReadOnlyList<string> Markers =>
        CachedLayout.Value.Select(x => x.marker).Distinct().ToArray();

    /// <summary>解決済みの値（marker→表示文字列）を背景様式へ重ねて1従業員分のPDFを生成する。</summary>
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
            var f = field with
            {
                XMm = 横補正切片Mm + 横補正係数 * field.XMm,
                幅Mm = 横補正係数 * field.幅Mm,
                YMm = 縦補正切片Mm + 縦補正係数 * field.YMm,
                高Mm = 縦補正係数 * field.高Mm,
                フォントPt = フォント係数 * field.フォントPt,
            };
            corrected.Add((marker, AdjustField(marker, f)));
        }
        return corrected;
    }

    // 上段金額行（支払金額を含む行）＝支払金額／給与所得控除後／所得控除計／源泉徴収額。
    private static bool 上段金額行(string marker)
        => marker.StartsWith("**支払金額_")
        || marker.StartsWith("**給与所得控除後の金額(調整控除後)_")
        || marker.StartsWith("**所得控除の額の合計額_")
        || marker.StartsWith("**源泉徴収額_");

    /// <summary>項目別のレイアウト微調整（給報・背景罫線の実測に基づく）。</summary>
    private static PdfOverlayField AdjustField(string marker, PdfOverlayField f)
    {
        // ── フォントサイズを源泉徴収票に合わせる（種別・上段金額は拡大、フリガナは縮小）──
        //    種別データ行は源泉より上寄りだったので少し下げる。
        if (marker == "**種別" || 上段金額行(marker))
            f = f with { フォントPt = f.フォントPt * 金額拡大, YMm = f.YMm + 種別行下げMm };
        if (marker.Contains("（フリガナ）"))
            f = f with { フォントPt = f.フォントPt * 0.82 };

        // ── 揃えを源泉徴収票に合わせる（桁別＝百万右/千中央/円左、右寄せ項目）──
        if (上段金額行(marker))
            f = f with { 揃え = marker.EndsWith("_円") ? PdfTextAlign.Left
                                : marker.EndsWith("_千") ? PdfTextAlign.Center
                                : PdfTextAlign.Right };
        else if (marker == "**特定親族特別控除額_千"
                 || marker.EndsWith("_国民年金保険料等の金額_円")
                 || marker.EndsWith("_基礎控除の額")
                 || marker.EndsWith("_旧長期損害保険料の金額_円")
                 || marker.EndsWith("_所得金額調整控除額"))
            f = f with { 揃え = PdfTextAlign.Right };

        // 社会保険料等の金額の「内」書き（小規模共済）が上の罫線に接触していたので少し下げる。
        if (marker == "**社会保険料等の金額_内")
            f = f with { YMm = f.YMm + 社保内下げMm };

        // 年度：○（丸枠）の中心へ。Xは面別（相似変換のまま＝左右2面それぞれの丸位置）、Yのみ丸の上下中心(実測7.37mm)へ。
        if (marker == "**年度")
            return f with { YMm = 7.37 - f.高Mm / 2 };

        // ヘッダー各欄（受給者番号／個人番号／役職名／フリガナ／氏名）：背景罫線実測のセル中心へ上下中央（約0.5文字下がる）。
        double? 中央Y =
            marker.StartsWith("**（受給者番号）") ? 15.02 :
            marker.StartsWith("**（個人番号）_個人番号") ? 18.67 :
            marker.StartsWith("**（役職名）") ? 23.35 :
            marker == "**氏名_（フリガナ）" ? 27.12 :
            marker == "**氏名_氏名" ? 30.95 :
            (double?)null;
        if (中央Y is double cy)
            return f with { YMm = cy - f.高Mm / 2 };

        // 生命保険料の金額の内訳／住宅借入金等特別控除の額の内訳：セル中心＋0.5文字下（源泉と同じ・背景罫線実測）。
        double? 内訳中央Y = null;
        if (marker.Contains("生命保険料の金額の内訳"))
            内訳中央Y = 98.04 + 0.5 * 文字Mm;
        else if (marker.Contains("住宅借入金等特別控除の額の内訳"))
            内訳中央Y = ((marker.Contains("(2回目)") || marker.Contains("（２回目）") || marker.Contains("可能額")) ? 108.77 : 103.63) + 0.5 * 文字Mm;
        if (内訳中央Y is double iy)
            return f with { YMm = iy - f.高Mm / 2 };

        // 控除対象配偶者・扶養親族・16歳未満の 氏名／フリガナ：枠内上下中央（背景罫線実測のセル中心）。
        if (氏名フリガナか(marker))
            return f with { YMm = 氏名フリガナ中央Y(marker) - f.高Mm / 2 };

        return f;
    }

    private const double 文字Mm = 2.5;
    private const double 種別行下げMm = 0.8;   // 種別データ行を源泉に合わせて少し下げる
    private const double フリガナ上げMm = 0.9; // 配偶者/扶養のフリガナを源泉に合わせて少し上げる
    private const double 氏名下げMm = -0.3;    // 配偶者/扶養の氏名が下寄りだったので少し上げて枠内中央に（負=上げ）
    private const double 社保内下げMm = 0.7;   // 社会保険料等「内」書きが上罫線に接触→下げる

    private static bool 氏名フリガナか(string marker)
        => (marker.Contains("控除対象配偶者_") || marker.Contains("控除対象扶養親族_") || marker.Contains("１６歳未満の扶養親族_"))
           && (marker.EndsWith("_氏名") || marker.Contains("（フリガナ）"));

    // 氏名／フリガナ セルの上下中央Y（給報・背景罫線実測。扶養/16歳未満は同一行＝N番目、行送り11.76mm）。
    private static double 氏名フリガナ中央Y(string marker)
    {
        bool フリガナ = marker.Contains("（フリガナ）");
        double 調整 = フリガナ ? -フリガナ上げMm : 氏名下げMm;   // フリガナは上げ、氏名は少し下げ
        if (marker.Contains("控除対象配偶者_"))
            return (フリガナ ? 112.77 : 115.56) + 調整;
        int n = 1;
        for (int k = 1; k <= 4; k++) if (marker.Contains($"_{k}_")) { n = k; break; }
        double 氏名c = 126.93 + (n - 1) * 11.76;
        return (フリガナ ? 氏名c - 3.48 : 氏名c) + 調整;
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
        => typeof(PayReportReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
