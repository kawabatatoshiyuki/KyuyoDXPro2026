using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 報酬、料金、契約金及び賞金の支払調書（現行 R_報酬）のPDFを生成する高レベルAPI（方式①の自己完結版）。
/// 様式（テンプレxlsx＝座標抽出元／背景PDF＝官製様式）をDLLに同梱し、実行時は純.NET
/// （PdfSharp＋OS同梱フォント）のみで動く。座標レイアウトは初回に1度だけ抽出してキャッシュする。
/// A4縦4面付け（1レコード＝1ページに同一4枚）は、同一マーカーが複数セルに現れる配置を全件描画して再現する。
/// </summary>
public sealed class RewardReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.RewardTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.RewardBackground.pdf";

    // 縦位置の補正：様式は退職源泉と同条件（A4横・fitToPage・同一余白・同一Extractor）。
    // 抽出器は行高を自然値（rowScale=1.0）で積むが、実際の背景印刷は fitToPage の縮尺がかかるため、
    // 上段は上寄り・下段は下寄りに線形ズレする。各項目の描画中心(Y+高/2)を背景枠中央へ合わせる
    // 一次補正 drawn_center = 切片 + 係数×抽出中心（退職源泉で確立した実測値と同型）。
    // 較正（2026-07-08）：背景PDF(R_報酬_背景.pdf)の枠罫線を0.1mm/pxで実測し、抽出中心→枠中心の
    // アフィン写像を上下段の両端明細行で最小二乗的に確定（上段row1:抽出39.48→枠43.05／下段row5:168.01→164.40）。
    // 旧値(3.76/0.972)は退職源泉から流用で傾きが不足し、下段が上へ寄る扇形ズレ＋上段明細のドリフトを生んでいた。
    private const double 縦補正切片Mm = 5.77;
    private const double 縦補正係数 = 0.9441;

    // 横位置の補正（右2面のみ）：抽出座標は右列＝左列+154.28mm と厳密に一様だが、背景PDF（Excel印刷）の
    // 左右パネル間隔は約151.4mm と約2.90mm狭い。このため右2面の全フィールドが背景罫線に対し一定量（≒2.90mm）
    // 右へずれ、特に右揃えの金額で目立つ。右列（XMm>閾値）だけ左へ一定量寄せて背景に合わせる（左列は不要）。
    // 左列の最大XMm=130.33／右列の最小XMm=159.89 なので閾値145で確実に分離する。
    private const double 右列判定XMm = 145.0;
    private const double 右列横補正Mm = 2.90;

    // 項目別レイアウト微調整（2026-07-08 指示）：タイトルの支給年を「年分」に合わせて下げ、
    // 支払を受ける者ブロック（住所・氏名・個人番号）と明細行を枠の縦中央へ、
    // 支払金額・源泉徴収税額の本体は枠内に収まるようフォント縮小する。4面すべてに同一適用。
    private const double 年数下げMm = 0.0;
    // タイトルの支給年（年数）を印字済み「年分」の枠内へ 3文字分ほど右へ（9.46pt 数字≈1.67mm/字）。
    private const double 年数右shiftMm = 5.0;
    private const double 受給者中央調整Mm = 1.2;
    private const double 明細中央調整Mm = 1.0;
    private const double 金額フォント率 = 0.74;
    // 住所・氏名が左罫線に寄り過ぎ → 0.5文字分ほど右へ（受給者・支払者とも）。
    private const double 住所氏名右shiftMm = 2.0;
    // 支払金額・源泉徴収税額のデータ部は1段目(内金＝内書き)・2段目(本体金額)とも右揃えにし、末尾を背景の「円」に
    // 合わせる。右揃え目標は幅Mmで表す（RewardPdfGenerator の描画は text右端 = XMm + 幅Mm - 内側余白Mm）。
    // セル右端R(=XMm+幅Mm)から「円」中心までは実測で約+3.7mm。本体は末尾が「円」の真下、内書きは「円」の1文字分左。
    private const double 内側余白Mm = 0.6;              // RewardPdfGenerator の内側余白と一致させる
    private const double 本体末尾延長Mm = 4.3;          // 本体2段目：末尾を「円」の下側へ（セル右端Rからの延長）
    private const double 内書き末尾延長Mm = 1.5;        // 内書き1段目：末尾を「円」の数字1文字分左へ
    // 支払を受ける者の「個人番号又は法人番号」の各桁が枠内で下寄り（下罫線ぎりぎり）→ 上下中央へ少し上げる。
    private const double 受給者番号上げMm = 1.0;
    // 本体金額（1件分の主金額）が明細行の下罫線に串刺しになる → 内書きより下・罫線より上へ。
    // 上段(内書き)と下段(本体)が近すぎたため上げ量を減らして本体を少し下げ、上段との縦間隔を広げる。
    private const double 本体金額上げMm = 1.55;
    // 内書き（内◯◯）は本体金額のすぐ上に重なりやすい（本体が長桁だと左へ伸びて上段の内書きと衝突）→
    // 上段の隅へ上げて本体との縦間隔を確保する（本体を下げるのと併せ上下段を分離）。内書き無し行の本体位置には影響しない。
    private const double 内書き上げMm = 1.3;
    // 区分・細目のデータ部は中央寄せをやめ左寄せにし、左罫線から0.5文字分(全角8.51pt≒1.5mm)空ける。
    // シフト量＝枠左端 + 0.5文字(1.5mm) − 内側余白(0.6) − 抽出X（左パネル実測：区分枠左7.70/細目枠左44.92、抽出X 5.62/44.11）。
    private const double 区分左寄せshiftMm = 2.98;
    private const double 細目左寄せshiftMm = 1.59;
    // 電話番号は会社名（名称）と同じ行の右側に重なるため、名称が長いと衝突する → 名称の下側へ少し下げる。
    private const double 電話上げMm = 0.5;

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>1件分の報酬支払調書（4面付け1ページ）のPDFバイト列を生成する。</summary>
    public byte[] CreatePdf(RewardReportData data)
    {
        using Stream background = OpenResource(BackgroundResource);
        return new RewardPdfGenerator().Generate(background, CachedLayout.Value, data);
    }

    private static IReadOnlyList<(string marker, PdfOverlayField field)> LoadLayout()
    {
        using Stream xlsx = OpenResource(TemplateResource);
        var raw = XlsxOverlayLayoutExtractor.ExtractAll(xlsx);

        var corrected = new List<(string marker, PdfOverlayField field)>(raw.Count);
        foreach ((string marker, PdfOverlayField field) in raw)
        {
            // 縦位置：描画中心を背景枠の中央へ合わせる一次補正（上段の上寄り・下段の下寄りを解消）。
            double newYc = 縦補正切片Mm + 縦補正係数 * (field.YMm + field.高Mm / 2);
            double newY = newYc - field.高Mm / 2;
            // 横位置：右2面（右列）のみ背景の狭いパネル間隔へ合わせて左へ寄せる。
            double newX = field.XMm - (field.XMm > 右列判定XMm ? 右列横補正Mm : 0.0);
            corrected.Add((marker, AdjustField(marker, field with { XMm = newX, YMm = newY })));
        }
        return corrected;
    }

    /// <summary>項目別のレイアウト微調整（2026-07-08 指示）。マーカー種別で縦位置・フォントを調整する。</summary>
    private static PdfOverlayField AdjustField(string marker, PdfOverlayField f)
    {
        // タイトルの支給年（年分）を下げて印字済みの「年分」に高さを合わせ、3文字分ほど右へ寄せる。
        if (marker == "**年数")
            return f with { YMm = f.YMm + 年数下げMm, XMm = f.XMm + 年数右shiftMm };

        // 支払を受ける者ブロック（住所・氏名・個人番号の各桁）を枠の縦中央へ。
        if (marker.StartsWith("**支払を受ける者_", StringComparison.Ordinal))
        {
            var g = f with { YMm = f.YMm + 受給者中央調整Mm };
            // 住所・氏名は左罫線に寄り過ぎ → 右へ（個人番号の桁は対象外）。
            if (marker.Contains("住所") || marker.Contains("氏名"))
                g = g with { XMm = g.XMm + 住所氏名右shiftMm };
            // 個人番号又は法人番号の各桁は下寄り → 枠内上下中央へ上げる。
            else if (marker.Contains("個人番号"))
                g = g with { YMm = g.YMm - 受給者番号上げMm };
            return g;
        }

        // 明細（区分・細目・金額系）を枠の縦中央へ。
        bool 明細 = marker.StartsWith("**区分_", StringComparison.Ordinal)
                  || marker.StartsWith("**細目_", StringComparison.Ordinal)
                  || marker.StartsWith("**源泉徴収税額_", StringComparison.Ordinal);
        if (明細)
        {
            var g = f with { YMm = f.YMm + 明細中央調整Mm };
            // 支払金額・源泉徴収税額の本体（2段目）：フォント縮小＋右揃えで末尾を「円」の下側へ。
            if (marker.EndsWith("支払金額_1", StringComparison.Ordinal)
                || marker.EndsWith("源泉徴収税額_1", StringComparison.Ordinal))
                g = g with
                {
                    YMm = g.YMm - 本体金額上げMm,
                    フォントPt = g.フォントPt * 金額フォント率,
                    揃え = PdfTextAlign.Right,
                    幅Mm = g.幅Mm + 本体末尾延長Mm + 内側余白Mm,
                };
            // 内書き（内未払＝支払金額側／内未徴収税額＝源泉側・1段目）：上段の隅へ上げ、右揃えで末尾を「円」の1文字左へ。
            else if (marker.Contains("内未", StringComparison.Ordinal))
                g = g with
                {
                    YMm = g.YMm - 内書き上げMm,
                    揃え = PdfTextAlign.Right,
                    幅Mm = g.幅Mm + 内書き末尾延長Mm + 内側余白Mm,
                };
            // 区分・細目は左寄せ＋左罫線から0.5文字分あける。
            else if (marker.StartsWith("**区分_", StringComparison.Ordinal))
                g = g with { 揃え = PdfTextAlign.Left, XMm = g.XMm + 区分左寄せshiftMm };
            else if (marker.StartsWith("**細目_", StringComparison.Ordinal))
                g = g with { 揃え = PdfTextAlign.Left, XMm = g.XMm + 細目左寄せshiftMm };
            return g;
        }

        // 支払者ブロック：住所・氏名を右へ、電話番号を上げる（番号の各桁は対象外）。
        if (marker.StartsWith("**支払者_", StringComparison.Ordinal))
        {
            if (marker.Contains("電話"))
                return f with { YMm = f.YMm - 電話上げMm };
            if (marker.Contains("住所") || marker.EndsWith("_名称_1", StringComparison.Ordinal))
                return f with { XMm = f.XMm + 住所氏名右shiftMm };
            return f;
        }
        return f;
    }

    private static Stream OpenResource(string name)
        => typeof(RewardReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
