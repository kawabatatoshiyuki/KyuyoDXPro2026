using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 給与所得に対する源泉徴収簿（現行 R_徴収簿）のPDFを生成する高レベルAPI（方式①＝官製様式オーバーレイ）。
/// マーカー→値の解決は呼び出し側（<see cref="PayCalcDXPro.Web"/> のビルダー）が行い、本クラスは解決済みの
/// 「marker → 表示文字列」辞書を背景PDF（官製様式）へ重ねるだけの汎用オーバーレイに徹する。
/// 座標はテンプレ(sheet「徴収簿」)から自動抽出。A4横1面（1従業員＝1ページ）。
///
/// 丸（甲/乙・扶養申告の有無・源泉控除対象配偶者の有無・寡婦/ひとり親・勤労学生・過不足）は、現行が
/// セルへ楕円を直接描く（Drawing.AddShape）ため、テンプレのマーカーには存在しない。官製様式上の固定
/// ラベルを囲む描画として、予約マーカー（"〇…"）＋<see cref="CircleCatalog"/> の実測mm座標で再現する。
/// </summary>
public sealed class WithholdingLedgerReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.WithholdingLedgerTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.WithholdingLedgerBackground.pdf";

    private const double 内側余白Mm = 0.4;

    // 背景PDF（Excel印刷）とマーカー座標系の相似変換。まず恒等（抽出座標そのまま）で描画し、
    // 背景罫線を実測して切片・係数を較正する（検証ループで詰める）。
    private const double 横補正切片Mm = 0.0;
    private const double 横補正係数 = 1.0;
    private const double 縦補正切片Mm = 0.0;
    private const double 縦補正係数 = 1.0;
    private const double フォント係数 = 1.0;

    // 所属：左罫線から0.5文字空ける（背景の仕切り罫線が抽出セル左より約1mm右のため、抽出基準では
    // やや広めの余白を取ると視覚上0.5文字になる）。
    private const double 所属左余白文字 = 0.9;

    // 住所は郵便番号行の下＝セル中央より下に置く。所属／職名／整理番号もこの高さに縦位置を揃える
    // （抽出セル中央からの下げ量。住所の中央≒他セル中央のため同値でそろう）。
    private const double 住所縦位置下げMm = 1.3;

    // 氏名欄（フリガナ・氏名・生年月日）の微調整。フリガナが上罫線に重なるので全体を下げる。
    // 3行がセル(H9.73)をほぼ埋めるため下げは行別（生年月日は下罫線を割らない範囲に抑える）。
    // フリガナはフォントを縮小して上に余白を作る（他様式と同じ手法）。横は0.5文字ぶん更に左へ。
    // フリガナ下げは郵便番号と同程度の上罫線余白になるよう設定。
    private const double フリガナ下げMm = 0.85;
    private const double 氏名下げMm = 0.4;
    private const double 生年月日下げMm = 0.15;
    private const double 氏名左shiftMm = 3.6;
    private const double フリガナ縮小 = 0.82;

    // 整理番号：背景右端が抽出座標より約7.5mm左にあるため、抽出中央のCenter描画だと視覚上右へずれる。左へ寄せる。
    private const double 整理番号左shiftMm = 7.5;

    // 左タイトルの「令和」「8」は抽出中央(X10.68mm)が背景の「年分」ラベル中央(≒12.6mm)より左のため、右へ寄せて中心を揃える。
    private const double 年タイトル右shiftMm = 2.0;

    // 左半分（給料・手当等／賞与等）テーブル：金額・人数は右寄せで右罫線から1文字空ける。
    private const double 右余白文字 = 1.0;

    // 差引徴収税額列だけは抽出セル右端が背景の右罫線（155.52mm実測）より外にあり、最終桁が罫線に掛かる。
    // この列のみ更に1文字ぶん左へ寄せる（ユーザー指示）。
    private const double 差引徴収税額左shift文字 = 1.0;

    // 右半分（年末調整結果 ①〜㉗・清算欄・右サブ列）の縦位置補正。抽出行ピッチ(3.82mm)が背景の実線ピッチ
    // (約3.72mm)よりわずかに大きく、抽出セル中央が下へ累積ドリフトする（①で-0.5mm→㉗で+1.3mm）。
    // 背景罫線を実測して「誤差 = 抽出中央Y − 背景枠中央Y = 切片 + 係数×抽出中央Y」を最小二乗フィット
    // （全行±0.07mm以内）。各値を YMm -= 誤差 で背景枠の上下中央へ寄せる。
    private const double 年調Y補正切片Mm = -2.14;
    private const double 年調Y補正係数 = 0.02149;

    // 右半分の数値2列を左へシフト（ユーザー指示）。金額列(X≈228.47)＝2.5文字左／税額列(X≈266.75・
    // 右サブ列「所得金額調整控除後の適用」の金額も同X帯＝右端の数値列)＝2文字左。右寄せのまま右端アンカーを詰めて左へ動かす。
    private const double 金額列X_Mm = 228.47;
    private const double 税額列X_Mm = 266.75;
    private const double 金額列左shift文字 = 2.5;
    private const double 税額列左shift文字 = 2.0;

    // ㉒㉓「差引課税給与所得金額(⑪-⑳)及び算出年税額」行の背景枠中央（罫線139.81〜145.01mm実測）。
    // この2マーカーは **_ 始まりで年末調整の線形補正ブランチに入らず、抽出中央が約1.8mm下へずれる。
    private const double 課税所得税額行中央Y_Mm = 142.41;

    // 前職分（中途就職者の前社給与・前社社保・前社会保険料控除後・前社源泉）行の背景枠中央（罫線195.2〜199.32mm実測）。
    // マーカーが **_ 始まりでどの微調整ブランチにも入らず、抽出のまま左寄せ＋下端に落ちて下罫線に重なる。
    private const double 前職分行中央Y_Mm = 197.26;

    // 「所得金額調整控除後の適用」欄の背景枠の左右中央（縦罫線256.61〜285.34mm実測）。
    // 無/有（1文字）の抽出中央は約277.6mmで枠中央より右へ寄るため、この中央へ Center 配置し直す。
    private const double 所得調整適用箱中央X_Mm = 270.98;
    // 給料の月別明細（月区分1〜12）の実線枠の水平罫線 mm（背景PDFを実測。ExcelのためY間隔が不均一）。
    // 行Nの上実線=月区分実線[N-1]／下実線=月区分実線[N]。枠中央＝点線のあるべき位置。
    // データは「上実線〜枠中央」の中央に置く（上実線と点線の中央）。
    private static readonly double[] 月区分実線Mm =
        { 56.04, 65.13, 72.59, 80.06, 87.49, 96.05, 104.96, 112.42, 119.85, 127.32, 134.75, 145.01, 152.70 };

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>抽出されたマーカー一覧（ビルダーが「どのマーカーを埋めるべきか」を参照するため）。</summary>
    public static IReadOnlyList<string> Markers =>
        CachedLayout.Value.Select(x => x.marker).Distinct().ToArray();

    /// <summary>
    /// 解決済みの値（marker→表示文字列）を背景様式へ重ねて1従業員分のPDFを生成する。
    /// 値が無い／空文字のマーカーは描画しない。予約マーカー "〇…" は丸（楕円）として描画する。
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

            // ①-0 右パネルの縦書きラベルだけを詰め直す（月別点線の引き直しは行わない）。
            //      背景（Excel印刷）の中央点線は枠中央から最大約0.73mm下へずれるが、この程度は目視できない。
            //      値セルを白抜きして点線を引き直す方式は、縦罫線との交点で必ずアーティファクト（段差・隙間）を
            //      生み罫線が乱れるため撤去した。値の配置は 月区分実線Mm 基準で点線と独立に決まる。
            if (page.Key == 0)
            {
                RedrawVerticalHeaderLabel(gfx);
            }

            // ① テキスト
            foreach ((string marker, PdfOverlayField f) in page)
            {
                if (!values.TryGetValue(marker, out string? text) || string.IsNullOrEmpty(text)) continue;

                var font = new XFont(JapaneseFontResolver.FamilyName, Math.Max(4.0, f.フォントPt));
                var rect = new XRect(
                    XUnit.FromMillimeter(f.XMm + 内側余白Mm).Point,
                    XUnit.FromMillimeter(f.YMm).Point,
                    XUnit.FromMillimeter(Math.Max(f.幅Mm - 内側余白Mm * 2, 2)).Point,
                    XUnit.FromMillimeter(Math.Max(f.高Mm, 3)).Point);

                // 改行を含む値（中途就退の年/月/日や摘要など）は行送りして描画する。
                if (text.Contains('\n')) DrawMultiline(gfx, text, font, rect, Format(f.揃え));
                else gfx.DrawString(text, font, XBrushes.Black, rect, Format(f.揃え));
            }
        }

        // ② 丸（楕円）：予約マーカー "〇…" が値辞書に存在すれば、その位置へ楕円を描く。ページは常に0（1面）。
        //    ビルダーは描きたい丸のキーだけを（任意の非空値で）辞書へ入れる。1キーで複数の楕円を描く場合あり（甲/乙）。
        if (doc.Pages.Count > 0)
        {
            using XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[0]);
            var pen = new XPen(XColors.Black, 0.8);
            foreach ((string key, IReadOnlyList<(double x, double y, double w, double h)> ovals) in CircleLayout.Value)
            {
                if (!values.TryGetValue(key, out string? sel) || string.IsNullOrEmpty(sel)) continue;
                foreach ((double x, double y, double w, double h) in ovals)
                    gfx.DrawEllipse(pen,
                        XUnit.FromMillimeter(x).Point, XUnit.FromMillimeter(y).Point,
                        XUnit.FromMillimeter(w).Point, XUnit.FromMillimeter(h).Point);
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

    private static void DrawMultiline(XGraphics gfx, string text, XFont font, XRect rect, XStringFormat fmt)
    {
        string[] lines = text.Split('\n');
        double lineH = rect.Height / lines.Length;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            var r = new XRect(rect.X, rect.Y + lineH * i, rect.Width, lineH);
            gfx.DrawString(lines[i], font, XBrushes.Black, r, fmt);
        }
    }

    // 点線引き直しのパラメータ。背景の点線は各行のサブ行仕切り（値サブ行と空白サブ行の境）。
    // 既存点線は枠中央から最大約0.73mm下へずれる（Excel印刷由来）。値セル内側だけを白抜きし枠中央へ再描画する。
    // 右パネルの縦書きラベル「扶養控除等の申告・各種控除額」。背景（Excel印刷）では14文字がセル高いっぱいに
    // 広がり、最上部「扶」が上罫線・最下部「額」が下罫線に接触する。白抜き→上下マージン付きで詰め直して再描画する。
    // セル実測（mm）：縦 32.4〜70.4（上下とも二重罫線）／横 155.8〜161.9（中心158.85）。白抜きは各罫線の内側へインセット。
    private const string 縦ラベル文字 = "扶養控除等の申告・各種控除額";
    private const double 縦ラベル左Mm = 156.2;      // 白抜き左（左罫線の右へインセット）
    private const double 縦ラベル右Mm = 161.5;      // 白抜き右（右罫線の左へインセット）
    private const double 縦ラベル上Mm = 33.0;       // 文字ブロックの上限（上罫線の下へインセット）
    private const double 縦ラベル下Mm = 69.8;       // 文字ブロックの下限（下罫線の上へインセット）
    private const double 縦ラベル上下余白Mm = 1.3;  // 文字ブロックの上下マージン（罫線に触れさせない）
    private const double 縦ラベルフォントPt = 6.2;
    // 白抜きは文字ブロックより広く取り、罫線ぎりぎりまで伸びていた旧文字の上端「扶」・下端「額」の断片も消す。
    // 上罫線(内側)の下端≒32.42mm／下罫線の上端≒70.43mm。その内側へわずかにインセットして罫線は残す。
    private const double 縦ラベル白抜き上Mm = 32.55;
    private const double 縦ラベル白抜き下Mm = 70.25;

    /// <summary>
    /// 右パネルの縦書きラベル「扶養控除等の申告・各種控除額」を、行間を詰めて上下罫線に掛からないよう再配置する。
    /// 背景（Excel印刷）では14文字がセル高いっぱいに広がり上下罫線に接触するため、セル内側を白抜きし、
    /// 上下マージンを確保したうえで1文字ずつ中央に積み直す。左右罫線・上下罫線には触れない。
    /// </summary>
    private static void RedrawVerticalHeaderLabel(XGraphics gfx)
    {
        // ① 既存の縦書きラベルを白抜き（各罫線の内側へインセット済み）。旧文字の上下端断片まで消すため白抜きは広めに取る。
        double wx = XUnit.FromMillimeter(縦ラベル左Mm).Point;
        double wy = XUnit.FromMillimeter(縦ラベル白抜き上Mm).Point;
        double ww = XUnit.FromMillimeter(縦ラベル右Mm - 縦ラベル左Mm).Point;
        double wh = XUnit.FromMillimeter(縦ラベル白抜き下Mm - 縦ラベル白抜き上Mm).Point;
        gfx.DrawRectangle(XBrushes.White, wx, wy, ww, wh);

        // ② 14文字を上下マージン付きで詰めて縦に積み直す。
        char[] chars = 縦ラベル文字.ToCharArray();
        int nCh = chars.Length;
        double interiorH = 縦ラベル下Mm - 縦ラベル上Mm;
        double blockH = interiorH - 2 * 縦ラベル上下余白Mm;
        double pitch = blockH / nCh;
        double cx = (縦ラベル左Mm + 縦ラベル右Mm) / 2.0;
        var font = new XFont(JapaneseFontResolver.FamilyName, 縦ラベルフォントPt);
        var fmt = new XStringFormat { Alignment = XStringAlignment.Center, LineAlignment = XLineAlignment.Center };
        for (int i = 0; i < nCh; i++)
        {
            double cy = 縦ラベル上Mm + 縦ラベル上下余白Mm + pitch * (i + 0.5);
            var rect = new XRect(
                XUnit.FromMillimeter(cx - 2).Point,
                XUnit.FromMillimeter(cy - pitch / 2).Point,
                XUnit.FromMillimeter(4).Point,
                XUnit.FromMillimeter(pitch).Point);
            gfx.DrawString(chars[i].ToString(), font, XBrushes.Black, rect, fmt);
        }
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
                YMm = 縦補正切片Mm + 縦補正係数 * field.YMm,
                高Mm = 縦補正係数 * field.高Mm,
                フォントPt = フォント係数 * field.フォントPt,
            };
            corrected.Add((marker, AdjustField(marker, f)));
        }
        return corrected;
    }

    /// <summary>項目別のレイアウト微調整（相似変換後に適用）。</summary>
    private static PdfOverlayField AdjustField(string marker, PdfOverlayField f)
    {
        double 文字Mm = f.フォントPt * 25.4 / 72.0;

        // 左半分（給料・手当等／賞与等）テーブルの各列。支給月日は横中央、金額・人数は右寄せ＋右罫線から1文字空け。
        // いずれも枠内で上下中央。給料の月別明細だけは値セルが行の上半分（下に空白サブ行）なので、高さを約2倍に
        // 広げて行全体の上下中央に置く（空白サブ行が値セルとほぼ同高＝2倍で行中央）。賞与明細（ピッチ=セル高＝単一
        // セル）と計行はセル全体が値枠なので据え置きで既に上下中央。
        if (marker.StartsWith("**給料・手当等") || marker.StartsWith("**賞与等"))
        {
            var g = f;
            // 給料の月別明細は、抽出Yが背景の実線グリッドと不均一にずれるため、実測実線から各行の目標中心を直接計算して
            // 配置する（上実線と枠中央＝点線位置の中央）。賞与明細・計行は据え置き（既に上下中央）。
            var rm = System.Text.RegularExpressions.Regex.Match(marker, @"^\*\*給料・手当等_月区分(\d+)_");
            if (rm.Success)
            {
                int n = int.Parse(rm.Groups[1].Value);
                if (n >= 1 && n < 月区分実線Mm.Length)
                {
                    double 上実線 = 月区分実線Mm[n - 1];
                    double 枠中央 = (上実線 + 月区分実線Mm[n]) / 2.0;
                    double データ中心 = (上実線 + 枠中央) / 2.0;
                    double rectH = Math.Max(f.高Mm, 3);
                    g = f with { YMm = データ中心 - rectH / 2.0 };
                }
            }
            if (marker.EndsWith("_支給月") || marker.EndsWith("_支給日") || marker.EndsWith("_月区分"))
                return g with { 揃え = PdfTextAlign.Center };
            // 差引徴収税額列は右罫線に最終桁が掛かるため、この列だけ更に1文字左へ寄せる。
            double 追加左shiftMm = marker.EndsWith("_差引徴収税額") ? 差引徴収税額左shift文字 * 文字Mm : 0;
            return g with { 揃え = PdfTextAlign.Right, 幅Mm = f.幅Mm - (右余白文字 * 文字Mm - 内側余白Mm) - 追加左shiftMm };
        }

        // ㉒㉓ 差引課税給与所得金額(⑪-⑳)及び算出年税額：マーカーが **_ 始まりで年末調整ブランチに入らず、
        // 抽出中央が枠中央より約1.8mm下（下罫線に接する）。背景枠中央へ絶対配置し、金額/税額列に合わせ
        // 右寄せ＋列シフト（この行は金額列＝㉒・税額列＝㉓そのもの）。
        if (marker.Contains("差引課税給与所得金額"))
        {
            var g = f with { YMm = 課税所得税額行中央Y_Mm - f.高Mm / 2.0 };
            double 追加左shiftMm =
                Math.Abs(f.XMm - 金額列X_Mm) < 2 ? 金額列左shift文字 * 文字Mm :
                Math.Abs(f.XMm - 税額列X_Mm) < 2 ? 税額列左shift文字 * 文字Mm : 0;
            return g with { 揃え = PdfTextAlign.Right, 幅Mm = f.幅Mm - (右余白文字 * 文字Mm - 内側余白Mm) - 追加左shiftMm };
        }

        // 前職分：上の給料／賞与「計」行と同じ列構成なので、同じく右寄せ＋右罫線から1文字空けて桁をそろえる。
        // 縦は抽出のままだと下罫線に重なるため、背景枠中央（195.2〜199.32mm実測）へ絶対配置して上へ寄せる。
        if (marker.StartsWith("**前職分_"))
        {
            var g = f with { YMm = 前職分行中央Y_Mm - f.高Mm / 2.0 };
            return g with { 揃え = PdfTextAlign.Right, 幅Mm = f.幅Mm - (右余白文字 * 文字Mm - 内側余白Mm) };
        }

        // 右半分（年末調整結果）：抽出中央の下方向ドリフトを線形補正して背景枠の上下中央へ寄せ、
        // 金額は右寄せにして縦の桁位置をそろえる。無/有の区分・計算根拠・通勤手当ラベルは金額でないため対象外。
        if (marker.StartsWith("**年末調整_"))
        {
            double 中央Y = f.YMm + f.高Mm / 2.0;
            double 誤差 = 年調Y補正切片Mm + 年調Y補正係数 * 中央Y;
            var g = f with { YMm = f.YMm - 誤差 };
            // 無/有：抽出中央が枠中央より右へ寄るため、背景枠（256.61〜285.34mm）の左右中央へ Center 配置。
            if (marker == "**年末調整_所得金額調整控除の適用")
                return g with { 揃え = PdfTextAlign.Center, XMm = 所得調整適用箱中央X_Mm - g.幅Mm / 2.0 };
            if (marker.EndsWith("_計算根拠")
                || marker.EndsWith("_非課税となる通勤手当"))
                return g;
            // 右寄せ＋右罫線から1文字空ける（左表と同じ扱い。桁が縦にそろい罫線に接しない）。
            // さらに列ごとに左へシフト（金額列2.5文字／税額列2文字）。右寄せなので幅を詰めると右端が左へ動く。
            double 追加左shiftMm =
                Math.Abs(f.XMm - 金額列X_Mm) < 2 ? 金額列左shift文字 * 文字Mm :
                Math.Abs(f.XMm - 税額列X_Mm) < 2 ? 税額列左shift文字 * 文字Mm : 0;
            return g with { 揃え = PdfTextAlign.Right, 幅Mm = f.幅Mm - (右余白文字 * 文字Mm - 内側余白Mm) - 追加左shiftMm };
        }

        // 扶養控除等の申告の各人数（当初行＝一般扶養・特定・特親・老人扶養・障害者）。抽出マーカー（AR13等）は
        // 各人数セルの左寄りの狭いサブセルのため、中央揃えでも視覚セルの左へ寄る。背景の縦罫線（実線）で決まる
        // 視覚セル中央（＝計行サンプルと同じ列中央）へ数値中心を合わせる。人ラベルは数値帯の上にあり重ならない。
        if (marker.StartsWith("**扶養控除等の申告_"))
        {
            double? 列中央X = marker switch
            {
                "**扶養控除等の申告_一般の控除対象扶養親族" => 189.5,
                "**扶養控除等の申告_特定扶養親族" => 196.95,
                "**扶養控除等の申告_特定親族" => 204.4,
                "**扶養控除等の申告_老人扶養親族_同居老親等" => 211.85,
                "**扶養控除等の申告_老人扶養親族_その他" => 219.3,
                "**扶養控除等の申告_一般の障害者" => 228.6,
                "**扶養控除等の申告_特別障害者" => 239.8,
                "**扶養控除等の申告_同居特別障害者" => 251.0,
                _ => (double?)null,
            };
            // Center揃えでは描画rect中心＝XMm+幅Mm/2に文字中心が来る（内側余白は左右で相殺）。
            return 列中央X is double cx
                ? f with { 揃え = PdfTextAlign.Center, XMm = cx - f.幅Mm / 2.0 }
                : f with { 揃え = PdfTextAlign.Center };
        }

        switch (marker)
        {
            // 左タイトルの「令和」「8」：背景の「年分」ラベルと横中心を揃えるため右へ寄せる（Centerのまま）。
            case "**年度":
            case "**年":
                return f with { XMm = f.XMm + 年タイトル右shiftMm };

            // 所属：左詰め・左罫線から0.5文字空ける・縦位置は住所に揃える。
            case "**所属":
                return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 所属左余白文字 * 文字Mm, YMm = f.YMm + 住所縦位置下げMm };

            // 職名：所属と同じく左詰め・左罫線から0.5文字空ける・縦位置は住所に揃える。
            case "**職名":
                return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 所属左余白文字 * 文字Mm, YMm = f.YMm + 住所縦位置下げMm };

            // 郵便番号：枠上端に接して上が切れるため0.5文字下げる。
            case "**住所_郵便番号":
                return f with { YMm = f.YMm + 0.5 * 文字Mm };

            // 住所データ：枠内で左詰め・左罫線から0.5文字空ける・住所枠の上下中央へ
            // （抽出セルは郵便番号行寄りで上寄りのため、枠中央まで下げる。背景罫線実測：枠21.6〜32.2mm中央26.9mm）。
            case "**住所_住所":
                return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 0.5 * 文字Mm, YMm = f.YMm + 住所縦位置下げMm };

            // フリガナ：上罫線に重なるのでフォント縮小＋下げで上に余白を作る。横は左シフト。
            case "**氏名_フリガナ":
                return f with { XMm = f.XMm - 氏名左shiftMm, YMm = f.YMm + フリガナ下げMm, フォントPt = f.フォントPt * フリガナ縮小 };

            // 氏名：下げ・左シフト。
            case "**氏名_氏名":
                return f with { XMm = f.XMm - 氏名左shiftMm, YMm = f.YMm + 氏名下げMm };

            // 生年月日：下端が下罫線に近いので下げは控えめ・左シフト。
            case "**氏名_生年月日":
                return f with { XMm = f.XMm - 氏名左shiftMm, YMm = f.YMm + 生年月日下げMm };

            // 整理番号：左右中央（背景右端が抽出座標より左のため左シフトで視覚中央へ）・縦位置は住所に揃える。
            case "**整理番号":
                return f with { 揃え = PdfTextAlign.Center, XMm = f.XMm - 整理番号左shiftMm, YMm = f.YMm + 住所縦位置下げMm };

            default:
                return f;
        }
    }

    // 丸（楕円）予約マーカー。ビルダーは該当キーを（任意の非空値で）辞書へ入れると丸が描かれる。
    // 相互排他の選択（甲か乙か等）はビルダー側の責務。甲/乙は左上ラベルと右の主従欄の2箇所を囲む。
    public const string 丸_甲 = "〇甲";
    public const string 丸_乙 = "〇乙";
    public const string 丸_配偶者有 = "〇配偶者有";       // 源泉控除対象配偶者「有」
    public const string 丸_配偶者無 = "〇配偶者無";
    public const string 丸_寡婦 = "〇寡婦";
    public const string 丸_ひとり親 = "〇ひとり親";
    public const string 丸_勤労学生有 = "〇勤労学生有";
    public const string 丸_勤労学生無 = "〇勤労学生無";
    public const string 丸_過納 = "〇過納";              // 差引超過額（還付）
    public const string 丸_不足 = "〇不足";              // 不足額（追加徴収）

    // 現行 R_徴収簿 の Drawing.AddShape（セル基準・mmオフセット x1,y1,x2,y2）を移植。
    // 座標抽出したセル左上mmへ各オフセットを足して楕円rectにする。1キーで複数楕円可（甲/乙）。
    private static readonly string[] 丸参照セル = { "A2", "A4", "AL14", "AP13", "BK13", "BN13", "AL67" };

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<(double x, double y, double w, double h)>>>
        CircleLayout = new(BuildCircleLayout);

    private static IReadOnlyDictionary<string, IReadOnlyList<(double, double, double, double)>> BuildCircleLayout()
    {
        using Stream xlsx = OpenResource(TemplateResource);
        var cells = XlsxOverlayLayoutExtractor.ExtractCells(xlsx, 丸参照セル);

        // セル左上mm + オフセット(x1,y1,x2,y2)mm → rect。相似変換（横/縦補正）はテキストと同一で適用。
        (double, double, double, double) Oval(string addr, double x1, double y1, double x2, double y2)
        {
            (double cx, double cy, double _, double __) = cells[addr];
            double x = 横補正切片Mm + 横補正係数 * (cx + x1);
            double y = 縦補正切片Mm + 縦補正係数 * (cy + y1);
            double w = 横補正係数 * (x2 - x1);
            double h = 縦補正係数 * (y2 - y1);
            return (x, y, w, h);
        }

        // 過不足（AL67）は背景の行位置が現行より約1行(この欄の行高≒2.5mm)下にずれるため上へ補正する（実測）。
        const double 過不足上補正 = -2.5;
        return new Dictionary<string, IReadOnlyList<(double, double, double, double)>>
        {
            // 甲/乙は主たる左上ラベルのみ囲む（現行の AL14 主従欄の第2楕円は意味が限定的なため省略）。
            [丸_甲] = new[] { Oval("A2", 2.95, 1.6, 9.55, 6.7) },
            // 乙欄の丸は甲欄と同サイズ（幅6.6/高5.1）。乙欄テキストはA4内で甲欄より上寄りのため、
            // 上下余白が均等になるよう甲より上へ（y1/y2 を約0.8mm上げ）。
            [丸_乙] = new[] { Oval("A4", 2.95, 0.8, 9.55, 5.9) },
            [丸_配偶者有] = new[] { Oval("AP13", 1.8, 0.7, 6.8, 6) },
            [丸_配偶者無] = new[] { Oval("AP13", 7, 0.7, 12, 6) },
            [丸_寡婦] = new[] { Oval("BK13", 3, 0.5, 8, 6) },
            [丸_ひとり親] = new[] { Oval("BK13", 9.1, 0.5, 14.1, 6) },
            [丸_勤労学生有] = new[] { Oval("BN13", 1.6, 0.7, 6.6, 5.9) },
            [丸_勤労学生無] = new[] { Oval("BN13", 6.8, 0.7, 11.8, 5.9) },
            // 過納＝「差引超過額」（5文字）／不足＝「不足額」（3文字）を囲む。元の offset(10-48 / 63-85) は
            // 現行Excelの AL67 基準で、ブラウザ抽出の AL67(左端≈166mm)とはズレ「超過額又は不」を囲んでいた。
            // 背景の文字インクを実測して較正（差引超過額≈166〜195.5mm／不足額≈204〜222mm・cx≈166mm）。
            [丸_過納] = new[] { Oval("AL67", 0, 0.5 + 過不足上補正, 29.5, 6 + 過不足上補正) },
            [丸_不足] = new[] { Oval("AL67", 38, 0.5 + 過不足上補正, 56, 6 + 過不足上補正) },
        };
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
        => typeof(WithholdingLedgerReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
