using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 不動産等の譲受けの対価の支払調書（現行 R_不動産譲受）のPDFを生成する高レベルAPI（方式①の自己完結版）。
/// 様式（テンプレxlsx＝座標抽出元／背景PDF＝官製様式）をDLLに同梱し、実行時は純.NET（PdfSharp＋OS同梱フォント）で動く。
/// A4横4面付け（1レコード＝1ページに同一4枚）。較正定数は不動産様式を初期値に流用し、目視で調整する。
/// </summary>
public sealed class RealEstateTransferReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.RealEstateTransferTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.RealEstateTransferBackground.pdf";

    // 縦位置の補正：抽出中心→背景枠中央への一次補正（不動産様式と同じく恒等が基準）。
    private const double 縦補正切片Mm = 0.0;
    private const double 縦補正係数 = 1.0;

    // 横位置の補正（右2面のみ）：背景の右パネルが抽出座標より左に寄っている分を左へ寄せる。
    // 実測で右面データが左面（背景枠一致）より約8mm右だったため増やして左上面と揃える。
    // さらにユーザー視認で0.7文字（≈1.81mm）右へ戻し 11.5→9.69。
    private const double 右列判定XMm = 145.0;
    private const double 右列横補正Mm = 9.69;

    // 下段パネル（左下・右下面）の一律縦補正：背景の下段パネルが純平行移動より一律上へ寄っている分、下段を上へ寄せる。
    // 上面と縦位置を完全一致させるため、上げ量を約1.79mm減らして下段を下げた（2.5→0.71＝実測で上面とデータ位置一致）。
    private const double 下段パネル判定YMm = 110.0;
    private const double 下段補正Mm = 0.71;

    private const double 内側余白Mm = 0.6;              // PdfGenerator の内側余白と一致
    private const double 文字Mm = 2.58;                 // 全角1文字(7.32pt)相当の横シフト単位

    private const double 金額末尾延長Mm = 0.0;          // 支払金額：末尾を「円」の手前へ（視認で較正）
    private const double 支払金額左shiftMm = 2 * 文字Mm; // 支払金額：右揃えのまま全体を2文字ぶん左へ
    private const double あっせん手数料左詰めMm = 0.0;  // あっせん手数料：右端を支払金額と揃える差（視認で較正）

    private const double 年右補正Mm = 2.4;              // タイトルの年：抽出セルは令和寄り→令和と年分の間の中央へ
    private const double 日付右補正Mm = 1.3;            // 取得年月日・支払確定年月日：可視枠が抽出セルより右にある分、中央へ寄せる

    // 個人番号又は法人番号：フォントサイズは確定済み[不動産]と同じ（フォント率0.74で縮小）。
    private const double 番号上げMm = 0.5;              // 受給者・支払者の番号を枠の上下中央へ
    private const double あっせん番号上げMm = 0.0;      // あっせんをした者の番号：LineAlignment.Center だけで枠中央（上げると上寄りになる＝実測で0）
    private const double 番号フォント率 = 0.74;         // 受給者・支払者の桁（幅2.30mm）は7.7ptだと枠をはみ出す→縮小（あっせんは幅4.59mmで対象外）
    private const double 受給者番号下げMm = 1.15;       // 受給者のみ下へ0.5文字（1文字=セル幅2.30mm）

    // 番号の桁グリッド補正：抽出マーカーのピッチが背景枠のピッチより広く、桁が進むほど右へずれる
    // （＝digit1が枠中心より左・digit13でほぼ一致）。桁番号Nに応じた線形補正で各桁を背景枠中央へ寄せる。
    // [不動産]の一律シフトはこのテンプレのドリフトに合わず digit1 が枠外へはみ出すため、線形補正に統一。
    // 受給者・支払者（幅2.30mm）実測：背景枠中心=108.54+(N-1)*2.207mm、マーカー中心=107.46+(N-1)*2.30mm ⇒ 1.08-0.093*(N-1)。
    private const double 番号始点補正Mm = 0.65;
    private const double 番号ピッチ差Mm = 0.093;
    // あっせん（幅4.59mm）実測：背景セル中心=41.3+(N-1)*4.34mm、マーカー中心=37.43+(N-1)*4.59mm ⇒ 3.9-0.25*(N-1)。
    private const double あっせん桁始点補正Mm = 3.9;
    private const double あっせん桁ピッチ差Mm = 0.25;

    // 明細の文字列列：中央→左寄せ。左罫線からの基準(1.8mm)に、列ごとの指定シフトを加える。
    private const double 明細基準shiftMm = 1.8;
    private const double 物件種類shiftMm = 明細基準shiftMm + 2.5 * 文字Mm;   // 2.5文字ぶん右へ
    private const double 物件所在地shiftMm = 明細基準shiftMm + 1.5 * 文字Mm; // 1.5文字ぶん右へ（収まらなければ生成器で縮小）
    private const double 細目shiftMm = 明細基準shiftMm + 0.5 * 文字Mm;       // 0.5文字ぶん右へ
    private const double 数量shiftMm = 明細基準shiftMm + 0.5 * 文字Mm;       // 0.5文字ぶん右へ

    private const double 摘要1右shiftMm = 3.5;          // 摘要1・摘要2とも「（摘要）」ラベルを避けて右へ寄せ、開始位置を摘要1に揃える
    private const double 電話上げMm = 1.5;              // 電話番号を下罫線から上へ
    private const double 電話右shiftMm = 1 * 文字Mm;    // 電話番号を1文字ぶん右へ
    private const double 住所フォントPt = 7.70;         // 支払者住所のフォントを他の住所に合わせる
    private const double 住所氏名右shiftMm = 4.3;       // 受給者・あっせん・支払者の住所/氏名/名称はデータセル左端がラベル列に食い込むため右へ寄せる

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>1件分の不動産譲受支払調書（4面付け1ページ）のPDFバイト列を生成する。</summary>
    public byte[] CreatePdf(RealEstateTransferReportData data)
    {
        using Stream background = OpenResource(BackgroundResource);
        return new RealEstateTransferPdfGenerator().Generate(background, CachedLayout.Value, data);
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
            double newX = field.XMm - (field.XMm > 右列判定XMm ? 右列横補正Mm : 0.0);
            corrected.Add((marker, AdjustField(marker, field with { XMm = newX, YMm = newY })));
        }
        return corrected;
    }

    /// <summary>項目別のレイアウト微調整（不動産譲受様式）。視認結果に応じて調整する。</summary>
    private static PdfOverlayField AdjustField(string marker, PdfOverlayField f)
    {
        // タイトルの年：令和と年分の間に、2桁でも収まるよう左右中央で置く（抽出セルは令和寄りなので右へ補正）。
        if (marker == "**年数")
            return f with { 揃え = PdfTextAlign.Center, XMm = f.XMm + 年右補正Mm };

        // 支払金額：右揃えで末尾を背景の「円」の手前へ。さらに全体を2文字ぶん左へ寄せる。
        if (marker.StartsWith("**支払金額_", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Right, 幅Mm = f.幅Mm + 金額末尾延長Mm + 内側余白Mm - 支払金額左shiftMm };

        // あっせん手数料：右揃えかつ右端を支払金額と揃える。
        if (marker.Contains("あっせん手数料", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Right, 幅Mm = f.幅Mm - あっせん手数料左詰めMm + 金額末尾延長Mm + 内側余白Mm };

        // 摘要1・摘要2：どちらも背景の「（摘要）」ラベルを避けて右へ寄せ、開始位置を摘要1に揃える。
        if (marker == "**（摘要）_摘要1_1" || marker == "**（摘要）_摘要2_1")
            return f with { XMm = f.XMm + 摘要1右shiftMm };

        // 明細の文字列列：中央→左寄せ、列ごとに指定量だけ右へ。ずらした分だけ幅も詰め、
        // セルの右境界（隣列の左端）を保つ＝生成器の fit-to-width が正しい可用幅で効く。
        if (marker.StartsWith("**物件の種類_", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 物件種類shiftMm, 幅Mm = f.幅Mm - 物件種類shiftMm };
        if (marker.StartsWith("**物件の所在地_", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 物件所在地shiftMm, 幅Mm = f.幅Mm - 物件所在地shiftMm };
        if (marker.StartsWith("**細目_", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 細目shiftMm, 幅Mm = f.幅Mm - 細目shiftMm };
        if (marker.StartsWith("**数量_", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 数量shiftMm, 幅Mm = f.幅Mm - 数量shiftMm };

        // 取得年月日・あっせん支払確定年月日：枠内で左右中央（可視枠が抽出セルより右にある分、右へ補正）。
        if (marker.StartsWith("**取得年月日_", StringComparison.Ordinal)
            || marker.EndsWith("_支払確定年月日_1", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Center, XMm = f.XMm + 日付右補正Mm };

        // 電話番号：下罫線に被るので上へ＋1文字ぶん右へ。
        if (marker.Contains("電話", StringComparison.Ordinal))
            return f with { YMm = f.YMm - 電話上げMm, XMm = f.XMm + 電話右shiftMm };

        // 支払者の住所は元のフォントが大きい → 他の住所に合わせる（右shift＋幅詰め）。
        if (marker == "**支払者_住所(居所)又は所在地_1")
            return f with { XMm = f.XMm + 住所氏名右shiftMm, 幅Mm = f.幅Mm - 住所氏名右shiftMm, フォントPt = 住所フォントPt };

        // 住所・氏名・名称：データセル左端がラベル列に食い込むため右へ寄せる（右shift＋幅詰め、電話は除外済）。
        if (marker.Contains("住所(居所)", StringComparison.Ordinal)
            || marker.EndsWith("氏名又は名称_1", StringComparison.Ordinal)
            || marker.EndsWith("氏名又は名称_名称_1", StringComparison.Ordinal))
            return f with { XMm = f.XMm + 住所氏名右shiftMm, 幅Mm = f.幅Mm - 住所氏名右shiftMm };

        // あっせん番号：抽出グリッドが背景の点線セルと起点・ピッチともズレる → 桁番号Nで線形補正し背景セル中央へ。
        // 相対シフトなので4面すべて（左右列とも）に有効。フォントは縮小せず（幅4.59mm）、上げのみ[不動産]と同じ。
        var am = System.Text.RegularExpressions.Regex.Match(
            marker, @"あっせんをした者_個人番号又は法人番号_(\d{1,2})番号_1$");
        if (am.Success)
        {
            int n = int.Parse(am.Groups[1].Value);
            double shift = あっせん桁始点補正Mm - あっせん桁ピッチ差Mm * (n - 1);
            return f with { XMm = f.XMm + shift, YMm = f.YMm - あっせん番号上げMm };
        }

        // 受給者・支払者の個人番号又は法人番号：フォントは[不動産]と同じ縮小(0.74)。
        // 横は桁番号Nの線形補正で背景枠中央へ（グリッドのドリフト対策）。縦は上げ(0.5)＋受給者のみ下げ(1.15)。
        // 中央寄せは生成器側（内側余白0.6・LineAlignment.Center）。
        if (marker.Contains("個人番号", StringComparison.Ordinal))
        {
            var nm = System.Text.RegularExpressions.Regex.Match(
                marker, @"個人番号又は法人番号_(\d{1,2})番号_1$");
            int n = nm.Success ? int.Parse(nm.Groups[1].Value) : 1;
            bool 受給者 = marker.Contains("支払を受ける者", StringComparison.Ordinal);
            double y = f.YMm - 番号上げMm;
            if (受給者) y += 受給者番号下げMm;
            double shift = 番号始点補正Mm - 番号ピッチ差Mm * (n - 1);
            return f with { XMm = f.XMm + shift, YMm = y, フォントPt = f.フォントPt * 番号フォント率 };
        }

        return f;
    }

    private static Stream OpenResource(string name)
        => typeof(RealEstateTransferReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
