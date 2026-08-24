using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 不動産等の売買又は貸付けのあっせん手数料の支払調書（現行 R_不動産あっせん）のPDFを生成する高レベルAPI（方式①の自己完結版）。
/// 様式（テンプレxlsx＝座標抽出元／背景PDF＝官製様式）をDLLに同梱し、実行時は純.NET（PdfSharp＋OS同梱フォント）で動く。
/// A4横4面付け（1レコード＝1ページに同一4枚）。較正定数は譲受様式を初期値に流用し、目視で調整する。
/// </summary>
public sealed class RealEstateMediationReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.RealEstateMediationTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.RealEstateMediationBackground.pdf";

    // 縦位置の補正：抽出中心→背景枠中央への一次補正（恒等が基準）。
    private const double 縦補正切片Mm = 0.0;
    private const double 縦補正係数 = 1.0;

    // 横位置の補正（右2面のみ）：背景の右パネルが抽出座標よりズレている分を補正。ドリフトはX依存（桁が右ほど大）
    // なので線形補正にする：区分(X=164.09)で7.3mm・支払金額(X=266.82)で8.5mm ⇒ 基準7.3＋勾配0.01168。
    private const double 右列判定XMm = 145.0;
    private const double 右列基準XMm = 164.09;
    private const double 右列横補正基準Mm = 7.3;
    private const double 右列横補正勾配 = (8.5 - 7.3) / (266.82 - 164.09);

    // 下段パネル（左下・右下面）の一律縦補正（目視で較正）。
    private const double 下段パネル判定YMm = 100.0;
    private const double 下段補正Mm = 2.8;

    private const double 内側余白Mm = 0.6;              // PdfGenerator の内側余白と一致
    private const double 文字Mm = 2.58;                 // 全角1文字(7.32pt)相当の横シフト単位

    private const double 金額末尾延長Mm = 0.0;          // 金額：末尾を「円」の手前へ（視認で較正）
    private const double 金額左shiftMm = 3.0 + 2 * 文字Mm; // 金額：右揃えのまま全体を左へ（ユーザー指示で更に2文字左）

    private const double 年右補正Mm = 0.0;              // タイトルの年数：抽出セル内で左右中央（必要なら右へ）
    private const double 年下げMm = 2.0;                // タイトルの年数：令和・年分の基準線に合わせて下げる
    private const double 日付右補正Mm = 0.0;            // 支払確定年月日：可視枠が抽出セルより右にある分、中央へ寄せる
    private const double 日付フォントPt = 7.55;         // 支払確定年月日：左右・上下段でフォントが混在→小さい方(7.55)に統一
    private const double 日付下げ1Mm = 0.6;             // 支払確定年月日1行目：可視行中央へ（ラベルは小フォントで回避）
    private const double 日付下げ2Mm = 1.46;            // 支払確定年月日2行目：マーカーが可視行より上寄り→可視行中央へ
    private const double 区分2下げMm = 1.5;             // 区分の明細2行目：マーカーセルが可視行より上寄り（実測1.46mm）→下げて中央へ
    private const double 支払金額2下げMm = 1.46;        // 支払金額の明細2行目：マーカーが可視行より上寄り→下げて上下中央へ

    // 個人番号又は法人番号：桁セル幅2.29mm→7.55ptだと枠をはみ出すため縮小（譲受と同じ0.74）。
    private const double 番号上げMm = 0.5;              // 受給者・支払者の番号を枠の上下中央へ
    private const double 番号フォント率 = 0.74;
    private const double 受給者番号下げMm = 2.3;        // 受給者番号のみ枠が下にある分だけ下げてマス目上下中央へ（実測）
    private const double 支払者番号下げMm = 0.74;       // 支払者番号：マス目中心91.8に対し描画中心91.06→0.74下げて上下中央（実測）

    // 番号の桁グリッド補正（桁番号Nの線形補正で背景マス目中央へ）。背景の罫線を実測して算出：
    // 背景マス中心 = 105.14 + (n-1)*2.24、描画中心 = 107.61 + (n-1)*2.29 ⇒ 補正 = -2.5 - 0.05*(n-1)。
    // 受給者・支払者はマス目グリッドが同一（実測一致）。
    private const double 番号始点補正Mm = -2.5;
    private const double 番号ピッチ差Mm = 0.05;

    // 明細の文字列列：中央→左寄せ。左罫線からの基準に列ごとの指定シフトを加える。
    private const double 明細基準shiftMm = 1.8;
    private const double 区分shiftMm = 3.0;             // 区分は左罫線から余裕を持たせる（右面の枠寄りで文字が罫線に触れないよう）
    // 物件の種類・所在地・数量：ユーザー指示で基準から0.5文字ぶん左へ。
    private const double 物件種類shiftMm = 明細基準shiftMm - 0.5 * 文字Mm;
    private const double 物件所在地shiftMm = 明細基準shiftMm - 0.5 * 文字Mm;
    private const double 数量shiftMm = 明細基準shiftMm - 0.5 * 文字Mm;

    private const double 摘要右shiftMm = 0.0;           // 摘要：「（摘要）」ラベルを避けて右へ（視認で較正）
    private const double 摘要1下げMm = 0.4;             // 摘要1行目：やや下げて2・3行目との行間を揃える（実測：1行目のみ0.4mm上寄り）
    private const double 電話上げMm = 1.5;              // 電話番号を下罫線から上へ
    private const double 電話右shiftMm = 1 * 文字Mm;    // 電話番号を1文字ぶん右へ
    private const double 住所氏名右shiftMm = 4.3;       // 支払者の住所/名称はデータセル左端がラベル列に食い込むため右へ
    private const double 受給者住所氏名右shiftMm = 4.3 - 1.5 * 文字Mm; // 支払を受ける者の住所/氏名：1文字＋0.5文字ぶん左へ（ユーザー指示）
    private const double 受給者住所氏名下げMm = 2.0;    // 支払を受ける者の住所/氏名：マーカーが可視セルより上寄り→下げて上下中央へ（実測）

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>1件分の不動産あっせん支払調書（4面付け1ページ）のPDFバイト列を生成する。</summary>
    public byte[] CreatePdf(RealEstateMediationReportData data)
    {
        using Stream background = OpenResource(BackgroundResource);
        return new RealEstateMediationPdfGenerator().Generate(background, CachedLayout.Value, data);
    }

    private static IReadOnlyList<(string marker, PdfOverlayField field)> LoadLayout()
    {
        using Stream xlsx = OpenResource(TemplateResource);
        var raw = XlsxOverlayLayoutExtractor.ExtractAll(xlsx);

        var corrected = new List<(string marker, PdfOverlayField field)>(raw.Count);
        foreach ((string marker, PdfOverlayField field) in raw)
        {
            double newYc = 縦補正切片Mm + 縦補正係数 * (field.YMm + field.高Mm / 2);
            double newY = newYc - field.高Mm / 2;
            if (newY > 下段パネル判定YMm) newY -= 下段補正Mm;
            double 右補正 = field.XMm > 右列判定XMm
                ? 右列横補正基準Mm + 右列横補正勾配 * (field.XMm - 右列基準XMm)
                : 0.0;
            double newX = field.XMm - 右補正;
            corrected.Add((marker, AdjustField(marker, field with { XMm = newX, YMm = newY })));
        }
        return corrected;
    }

    /// <summary>項目別のレイアウト微調整（不動産あっせん様式）。視認結果に応じて調整する。</summary>
    private static PdfOverlayField AdjustField(string marker, PdfOverlayField f)
    {
        // タイトルの年数：「令和」と「年分」の間で左右中央。基準線に合わせて少し下げる。
        if (marker == "**年数")
            return f with { 揃え = PdfTextAlign.Center, XMm = f.XMm + 年右補正Mm, YMm = f.YMm + 年下げMm };

        // 支払金額・取引金額：右揃えで末尾を背景の「円」の手前へ。支払金額の2行目はマーカーが可視行より上寄りのため下げて上下中央に。
        if (marker.EndsWith("支払金額", StringComparison.Ordinal) || marker.EndsWith("取引金額", StringComparison.Ordinal))
        {
            double dy = marker == "**支払金額_2支払金額" ? 支払金額2下げMm : 0.0;
            return f with { 揃え = PdfTextAlign.Right, YMm = f.YMm + dy, 幅Mm = f.幅Mm + 金額末尾延長Mm + 内側余白Mm - 金額左shiftMm };
        }

        // 摘要：背景の「（摘要）」ラベルを避けて右へ寄せる。1行目はやや下げて2・3行目と行間を揃える。
        if (marker.StartsWith("**（摘要）_摘要", StringComparison.Ordinal))
        {
            double dy = marker == "**（摘要）_摘要1" ? 摘要1下げMm : 0.0;
            return f with { XMm = f.XMm + 摘要右shiftMm, YMm = f.YMm + dy };
        }

        // 明細の文字列列：中央→左寄せ、列ごとに指定量だけ右へ。ずらした分だけ幅も詰める。
        // 区分の2行目はマーカーが可視行より上寄りのため下げて上下中央にする。
        if (marker.EndsWith("区分", StringComparison.Ordinal))
        {
            double dy = marker.Contains("_2区分", StringComparison.Ordinal) ? 区分2下げMm : 0.0;
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 区分shiftMm, YMm = f.YMm + dy, 幅Mm = f.幅Mm - 区分shiftMm };
        }
        if (marker.EndsWith("物件の種類", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 物件種類shiftMm, 幅Mm = f.幅Mm - 物件種類shiftMm };
        if (marker.EndsWith("物件の所在地", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 物件所在地shiftMm, 幅Mm = f.幅Mm - 物件所在地shiftMm };
        if (marker.EndsWith("数量", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 数量shiftMm, 幅Mm = f.幅Mm - 数量shiftMm };

        // 支払確定年月日：フォントを小さい方(7.55pt)に統一し、枠内で左右・上下中央に配置（行ごとに可視行中央へ）。
        if (marker.EndsWith("支払確定年月日", StringComparison.Ordinal))
        {
            double dy = marker == "**支払確定年月日_2支払確定年月日" ? 日付下げ2Mm : 日付下げ1Mm;
            return f with { 揃え = PdfTextAlign.Center, XMm = f.XMm + 日付右補正Mm, YMm = f.YMm + dy, フォントPt = 日付フォントPt };
        }

        // 電話番号：下罫線に被るので上へ＋1文字ぶん右へ。
        if (marker.Contains("電話", StringComparison.Ordinal))
            return f with { YMm = f.YMm - 電話上げMm, XMm = f.XMm + 電話右shiftMm };

        // 支払を受ける者の住所・氏名：ユーザー指示で1文字分左＋マーカーが可視セルより上寄りのため下げて上下中央へ。
        if (marker == "**支払を受ける者_住所(居所)又は所在地" || marker == "**支払を受ける者_氏名又は名称")
            return f with { XMm = f.XMm + 受給者住所氏名右shiftMm, YMm = f.YMm + 受給者住所氏名下げMm, 幅Mm = f.幅Mm - 受給者住所氏名右shiftMm };

        // 支払者の住所・名称：データセル左端がラベル列に食い込むため右へ寄せる（電話は除外済、上下中央は良好）。
        if (marker.Contains("住所(居所)", StringComparison.Ordinal)
            || marker.EndsWith("氏名又は名称", StringComparison.Ordinal)
            || marker.EndsWith("氏名又は名称_名称", StringComparison.Ordinal))
            return f with { XMm = f.XMm + 住所氏名右shiftMm, 幅Mm = f.幅Mm - 住所氏名右shiftMm };

        // 受給者・支払者の個人番号又は法人番号：フォント縮小(0.74)＋桁番号Nの線形補正で背景枠中央へ。縦は上げ(0.5)＋受給者のみ下げ。
        if (marker.Contains("個人番号", StringComparison.Ordinal))
        {
            var nm = System.Text.RegularExpressions.Regex.Match(marker, @"個人番号又は法人番号_(\d{1,2})番号$");
            int n = nm.Success ? int.Parse(nm.Groups[1].Value) : 1;
            bool 受給者 = marker.Contains("支払を受ける者", StringComparison.Ordinal);
            double y = f.YMm - 番号上げMm;
            if (受給者) y += 受給者番号下げMm;
            else y += 支払者番号下げMm;
            double shift = 番号始点補正Mm - 番号ピッチ差Mm * (n - 1);
            return f with { XMm = f.XMm + shift, YMm = y, フォントPt = f.フォントPt * 番号フォント率 };
        }

        return f;
    }

    private static Stream OpenResource(string name)
        => typeof(RealEstateMediationReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
