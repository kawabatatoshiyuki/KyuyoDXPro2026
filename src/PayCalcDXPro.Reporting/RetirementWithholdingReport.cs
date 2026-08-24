using System.Text.RegularExpressions;
using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 退職所得の源泉徴収票・特別徴収票のPDFを生成する高レベルAPI（方式①の自己完結版）。
/// 様式（テンプレxlsx＝座標抽出元／背景PDF＝官製様式）をDLLに同梱し、実行時は純.NET
/// （PdfSharp＋OS同梱フォント）のみで動く。座標レイアウトは初回に1度だけ抽出してキャッシュする。
/// </summary>
public sealed class RetirementWithholdingReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.RetirementTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.RetirementBackground.pdf";

    // 4面付けの左パネル（用紙左半分）は、列幅フィット近似の残差で値が約3.2mm左へずれる
    // （右パネルは用紙右端がキャリブレーションのピンになるため正確）。左パネルのみ右へ補正する。
    private const double 用紙幅Mm = 297.0;          // A4横
    private const double 左パネル補正Mm = 3.2;

    // 個人番号の桁枠（個人番号12桁／個人番号又は法人番号13桁）は、列幅フィット近似のピッチ誤差で
    // 桁が枠に対して右へ線形にドリフトする（枠中央からずれる）。桁マーカーは列幅モデルに依らず、
    // 背景様式の実測グリッド（左端枠の中央＝用紙左から約41.2mm、枠ピッチ約5.08mm。受給者・支払者とも
    // 同じ列 M,O,Q… 起点で共通）へ直接中央配置する。※背景PDF差し替え時は要再計測。
    private const double 桁枠先頭中央Mm = 41.2;
    private const double 桁枠ピッチMm = 5.08;
    private static readonly Regex 受給者桁 = new(@"^\*\*支払を受ける者_個人番号_(\d{1,2})桁$", RegexOptions.Compiled);
    private static readonly Regex 支払者桁 = new(@"^\*\*支払者_個人番号又は法人番号_(\d{1,2})桁$", RegexOptions.Compiled);

    // 縦位置の補正：抽出器は行高を自然値（rowScale=1.0）で積むため、実際の印刷（僅かに縮尺）と
    // 線形にずれる＝上段は上寄り・下段は下寄りになる。各項目の描画中心(Y+高/2)を背景枠の中央へ
    // 合わせる一次補正 drawn_center = 切片 + 係数×抽出中心（実測回帰・最大残差0.08mm）。
    private const double 縦補正切片Mm = 0.79;
    private const double 縦補正係数 = 0.990;

    private static readonly Lazy<IReadOnlyList<(string marker, PdfOverlayField field)>> CachedLayout = new(LoadLayout);

    /// <summary>1件分の退職源泉票（4面付け1ページ）のPDFバイト列を生成する。</summary>
    public byte[] CreatePdf(RetirementWithholdingReportData data)
    {
        using Stream background = OpenResource(BackgroundResource);
        return new RetirementWithholdingPdfGenerator().Generate(background, CachedLayout.Value, data);
    }

    private static IReadOnlyList<(string marker, PdfOverlayField field)> LoadLayout()
    {
        using Stream xlsx = OpenResource(TemplateResource);
        var raw = XlsxOverlayLayoutExtractor.ExtractAll(xlsx);

        var corrected = new List<(string marker, PdfOverlayField field)>(raw.Count);
        foreach ((string marker, PdfOverlayField field) in raw)
        {
            // 横位置：個人番号の桁マーカーは実測グリッドの枠中央へ直接配置（列幅モデルのドリフト回避）。
            // それ以外は、左パネル（XMm が用紙中央より左）だけ右へずらして罫線と整合させる。
            double newX = 桁枠中央X(marker, out double centerX)
                ? centerX - field.幅Mm / 2
                : (field.XMm < 用紙幅Mm / 2 ? field.XMm + 左パネル補正Mm : field.XMm);

            // 縦位置：描画中心を背景枠の中央へ合わせる一次補正（上段の上寄り・下段の下寄りを解消）。
            double newYc = 縦補正切片Mm + 縦補正係数 * (field.YMm + field.高Mm / 2);
            double newY = newYc - field.高Mm / 2;

            corrected.Add((marker, field with { XMm = newX, YMm = newY }));
        }
        return corrected;
    }

    /// <summary>個人番号の桁マーカーなら、その枠（左から数えた位置）の中央X（mm）を返す。</summary>
    private static bool 桁枠中央X(string marker, out double centerX)
    {
        centerX = 0;
        Match j = 受給者桁.Match(marker);   // 12桁：桁12＝左端
        if (j.Success) { centerX = 枠中央(12, int.Parse(j.Groups[1].Value)); return true; }
        Match p = 支払者桁.Match(marker);   // 13桁：桁13＝左端
        if (p.Success) { centerX = 枠中央(13, int.Parse(p.Groups[1].Value)); return true; }
        return false;

        static double 枠中央(int 総桁, int 桁) => 桁枠先頭中央Mm + (総桁 - 桁) * 桁枠ピッチMm;
    }

    private static Stream OpenResource(string name)
        => typeof(RetirementWithholdingReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
