using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 法定調書合計表のPDFを生成する高レベルAPI（方式①の完全自己完結版）。
/// 様式（テンプレxlsx＝座標抽出元／背景PDF＝官製様式）をDLLに同梱しているため、
/// 実行時は外部ファイル・外部プロセス（LibreOffice等）・顧客導入物に一切依存しない。
/// 座標レイアウトは初回に1度だけ抽出してキャッシュする。
/// </summary>
public sealed class LegalRecordSummaryReport
{
    private const string TemplateResource = "PayCalcDXPro.Reporting.Assets.LegalRecordTemplate.xlsx";
    private const string BackgroundResource = "PayCalcDXPro.Reporting.Assets.LegalRecordBackground.pdf";

    private static readonly Lazy<IReadOnlyDictionary<string, PdfOverlayField>> CachedLayout = new(LoadLayout);

    /// <summary>集計と提出者情報からPDFバイト列を生成する。</summary>
    public byte[] CreatePdf(LegalRecordSummary summary, 法定調書提出者情報 提出者)
    {
        using Stream background = OpenResource(BackgroundResource);
        return new LegalRecordSummaryPdfGenerator().Generate(background, CachedLayout.Value, summary, 提出者);
    }

    private static IReadOnlyDictionary<string, PdfOverlayField> LoadLayout()
    {
        using Stream xlsx = OpenResource(TemplateResource);
        return XlsxOverlayLayoutExtractor.Extract(xlsx);
    }

    private static Stream OpenResource(string name)
        => typeof(LegalRecordSummaryReport).Assembly.GetManifestResourceStream(name)
           ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {name}");
}
