using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 不動産の使用料等の支払調書（現行 R_不動産）のPDFを生成する高レベルAPI（方式①の自己完結版）。
/// 様式（テンプレxlsx＝座標抽出元／背景PDF＝官製様式）をDLLに同梱し、実行時は純.NET
/// （PdfSharp＋OS同梱フォント）のみで動く。座標レイアウトは初回に1度だけ抽出してキャッシュする。
/// A4横4面付け（1レコード＝1ページに同一4枚）は、同一マーカーが複数セルに現れる配置を全件描画して再現する。
/// </summary>
public sealed class RealEstateReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.RealEstateTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.RealEstateBackground.pdf";

    // 縦位置の補正（報酬・退職源泉と同型）：抽出器は行高を自然値で積むが、背景は fitToPage の縮尺がかかるため
    // 上段は上寄り・下段は下寄りに線形ズレしうる。描画中心(Y+高/2)を背景枠中央へ合わせる一次補正
    // drawn_center = 切片 + 係数×抽出中心。※不動産様式では抽出生座標が背景枠中央とほぼ一致（実測で
    // 区分1:生32.09/枠32.11、支払者名称:生87.80/枠87.93）＝恒等でよい。報酬の値を流用すると総崩れになる。
    private const double 縦補正切片Mm = 0.0;
    private const double 縦補正係数 = 1.0;

    // 横位置の補正（右2面のみ）：背景の左右パネル間隔が抽出座標より狭い場合、右列を左へ寄せる。
    // ※不動産様式で較正する（初期値は補正なし＝0）。
    private const double 右列判定XMm = 145.0;
    // マーカーは左→右が正確に+152.69mmの平行移動だが、背景の右パネルは約3.56mm左に寄っている（実測：
    // 左受給者箱左端105.73/右254.86、差153.66に対しマーカー差152.69）。右列データが約1文字分右に見えるので左へ補正。
    private const double 右列横補正Mm = 3.56;

    // 項目別レイアウト微調整（2026-07-13 指示）：
    // ① 支払金額のデータ部を「円」の手前まで右詰め（現状は左揃え）。右揃え目標は幅Mmで表す
    //    （RealEstatePdfGenerator の描画は text右端 = XMm + 幅Mm − 内側余白Mm）。末尾延長Mmで微調整。
    // ② あっせん手数料は現状セル右端が支払金額より あっせん手数料左詰めMm だけ右にある。右揃えかつ
    //    右端を上部の支払金額と揃える（幅からセル右端差を差し引く）＝縦に並ぶ金額が同じ位置で止まる。
    // ③ 明細の区分・物件の所在地・細目・計算の基礎は中央寄せ→左寄せにし、左罫線から0.5文字あける。
    // ④ 支払を受ける者・支払者・あっせんをした者の個人番号又は法人番号の各桁を枠の上下中央へ。
    // ⑤ 支払者の電話番号が下罫線に被る→上へ（会社名が長い場合の干渉を避けるため控えめに）。
    private const double 内側余白Mm = 0.6;              // RealEstatePdfGenerator の内側余白と一致させる
    private const double 金額末尾延長Mm = 0.0;          // 支払金額：末尾を「円」の手前へ（視認で較正）
    private const double あっせん手数料左詰めMm = 4.59; // あっせん手数料セル右端(136.16) − 支払金額セル右端(131.57)。右端を揃える
    private const double 番号上げMm = 0.5;              // 受給者・支払者の番号（H≈4.53）を枠中央へ
    private const double あっせん番号上げMm = 1.0;      // あっせんをした者の番号（H≈6.34）を枠中央へ
    private const double 番号フォント率 = 0.74;         // 受給者・支払者の番号桁（幅2.30mm）は7.7ptだと右端が枠をはみ出す→縮小（あっせんは幅4.59mmで対象外）
    private const double 番号左シフトMm = 0.6;          // 背景の桁グリッドがマーカーより約0.6mm左にある（実測）→受給者・支払者の桁を左へ寄せて背景セル中央へ
    private const double 番号追加左Mm = 0.69;           // 追加調整：受給者・支払者とも左へ0.3文字（1文字=セル幅2.30mm）
    private const double 受給者番号下げMm = 1.15;       // 追加調整：受給者のみ下へ0.5文字（1文字=セル幅2.30mm）
    // 下段パネルの一律縦補正：マーカーは上段→下段が正確に+107.24mmの平行移動だが、背景PDFは下段パネルが
    // 一律で上へ寄っている（fitToPage起因）。このため下段のオーバーレイは背景枠より下に見える。
    // 上段最大B=92.73／下段最小Y=112.24とギャップが明確なので、Y>110の全フィールドを一律で上へ寄せて背景に合わせる。
    private const double 下段パネル判定YMm = 110.0;
    private const double 下段補正Mm = 2.5;              // 下段パネル全体を上へ。背景の下段は純平行移動より一律約2.5mm(27px)上（受給者・明細・支払者いずれも同量、実測）
    private const double 明細左寄せshiftMm = 1.8;       // 区分/物件/細目/計算：左寄せ＋左罫線から0.5文字（背景は抽出座標より約0.9mm右のため実測で較正）
    private const double 電話上げMm = 1.5;              // 電話番号を下罫線から上へ（会社名と干渉しない控えめ量）
    private const double 住所フォントPt = 7.70;         // 支払者住所は元9.41ptと大きい→他の住所（受給者・あっせん=7.70pt）に合わせる

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>1件分の不動産支払調書（4面付け1ページ）のPDFバイト列を生成する。</summary>
    public byte[] CreatePdf(RealEstateReportData data)
    {
        using Stream background = OpenResource(BackgroundResource);
        return new RealEstatePdfGenerator().Generate(background, CachedLayout.Value, data);
    }

    private static IReadOnlyList<(string marker, PdfOverlayField field)> LoadLayout()
    {
        using Stream xlsx = OpenResource(TemplateResource);
        var raw = XlsxOverlayLayoutExtractor.ExtractAll(xlsx);

        var corrected = new List<(string marker, PdfOverlayField field)>(raw.Count);
        foreach ((string marker, PdfOverlayField field) in raw)
        {
            // 縦位置：描画中心を背景枠の中央へ合わせる一次補正。
            double newYc = 縦補正切片Mm + 縦補正係数 * (field.YMm + field.高Mm / 2);
            double newY = newYc - field.高Mm / 2;
            // 下段パネルは背景が一律で上へ寄っているため、全フィールドを一律で上へ補正して合わせる。
            if (newY > 下段パネル判定YMm) newY -= 下段補正Mm;
            // 横位置：右2面（右列）のみ背景の狭いパネル間隔へ合わせて左へ寄せる。
            double newX = field.XMm - (field.XMm > 右列判定XMm ? 右列横補正Mm : 0.0);
            corrected.Add((marker, AdjustField(marker, field with { XMm = newX, YMm = newY })));
        }
        return corrected;
    }

    /// <summary>項目別のレイアウト微調整（不動産様式・2026-07-13 指示）。視認結果に応じて調整を追加する。</summary>
    private static PdfOverlayField AdjustField(string marker, PdfOverlayField f)
    {
        // ① 支払金額：右揃えで末尾を背景の「円」の手前へ。
        if (marker.StartsWith("**支払金額_", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Right, 幅Mm = f.幅Mm + 金額末尾延長Mm + 内側余白Mm };

        // ② あっせん手数料：右揃えかつ右端を上部の支払金額と揃える（セル右端の差だけ幅を詰める）。
        if (marker.Contains("あっせん手数料", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Right, 幅Mm = f.幅Mm - あっせん手数料左詰めMm + 金額末尾延長Mm + 内側余白Mm };

        // ③ 明細（区分・物件の所在地・細目・計算の基礎）：中央→左寄せ、左罫線から0.5文字あける。
        if (marker.StartsWith("**区分_", StringComparison.Ordinal)
            || marker.StartsWith("**物件の所在地_", StringComparison.Ordinal)
            || marker.StartsWith("**細目_", StringComparison.Ordinal)
            || marker.StartsWith("**計算の基礎_", StringComparison.Ordinal))
            return f with { 揃え = PdfTextAlign.Left, XMm = f.XMm + 明細左寄せshiftMm };

        // ⑤ 電話番号が下罫線に被る → 上へ（会社名との干渉を避け控えめに）。
        if (marker.Contains("電話", StringComparison.Ordinal))
            return f with { YMm = f.YMm - 電話上げMm };

        // ⑥ 支払者の住所は元のフォントが大きい（9.41pt）→他の住所（7.70pt）に合わせる。
        if (marker == "**支払者_住所(居所)又は所在地_1")
            return f with { フォントPt = 住所フォントPt };

        // ④ 個人番号又は法人番号の各桁が枠内で下寄り → 枠の上下中央へ上げる。
        //    受給者・支払者は桁セルが狭い（幅2.30mm）ためフォントを縮小して枠内へ収める（あっせんは幅4.59mmで対象外）。
        if (marker.Contains("個人番号", StringComparison.Ordinal))
        {
            bool あっせん = marker.Contains("あっせんをした者", StringComparison.Ordinal);
            bool 受給者 = marker.Contains("支払を受ける者", StringComparison.Ordinal);
            double 上げ = あっせん ? あっせん番号上げMm : 番号上げMm;
            double pt = あっせん ? f.フォントPt : f.フォントPt * 番号フォント率;
            double x = f.XMm;
            double y = f.YMm - 上げ;
            if (!あっせん)
            {
                x -= 番号左シフトMm + 番号追加左Mm;   // 受給者・支払者とも左へ（0.6mm＋0.3文字）
                if (受給者) y += 受給者番号下げMm;      // 受給者のみ下へ0.5文字
                // 下段パネルの縦補正は LoadLayout で全フィールド一律適用済み。
            }
            return f with { XMm = x, YMm = y, フォントPt = pt };
        }

        return f;
    }

    private static Stream OpenResource(string name)
        => typeof(RealEstateReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
