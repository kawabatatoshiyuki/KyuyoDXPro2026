using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using PayCalcDXPro.Reporting;
using PayCalcDXPro.Reporting.Models;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 法定調書合計表の完全自己完結生成（方式①、同梱アセットのみ）。
/// LibreOfficeも外部ファイルも使わず、DLL埋め込みのテンプレ／背景PDFだけでA4 PDFを生成できることを確認する。
/// </summary>
public class LegalRecordReportTests
{
    [Fact]
    public void 埋め込みアセットだけでA4のPDFを生成する()
    {
        LegalRecordSummary summary = new LegalRecordSummaryCalculator().Calculate(
            給与: new[]
            {
                new 給与支払行 { 給与賞与計 = 5_000_000m, 源泉計 = 120_000m, 源泉徴収票提出 = true },
                new 給与支払行 { 給与賞与計 = 2_000_000m, 源泉計 = 0m },
            },
            退職: new[] { new 退職支払行 { 提出 = true, 支払金額1 = 3_000_000m, 源泉徴収税額1 = 80_000m } },
            報酬: Array.Empty<報酬支払行>(),
            不動産: new[] { new 不動産支払行 { 提出要 = true, 支払金額 = new[] { 1_200_000m } } },
            譲受: Array.Empty<不動産支払行>(),
            あっせん: Array.Empty<不動産支払行>());

        var 提出者 = new 法定調書提出者情報
        {
            年 = Wareki.年数(2025), 署番号 = "00123", 事業種目 = "ソフトウェア業",
            会社名 = "周豊システム株式会社", 事業所番号 = "123456789012", 番号区分 = "2",
        };

        byte[] pdf = new LegalRecordSummaryReport().CreatePdf(summary, 提出者);

        Assert.True(pdf.Length > 10_000, "PDFが生成されている");

        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(1, doc.PageCount);
        Assert.Equal(210, doc.Pages[0].Width.Millimeter, 1.0);
        Assert.Equal(297, doc.Pages[0].Height.Millimeter, 1.0);
    }

    [Fact]
    public void レイアウトはキャッシュされ複数回生成できる()
    {
        var report = new LegalRecordSummaryReport();
        LegalRecordSummary empty = new LegalRecordSummaryCalculator().Calculate(
            Array.Empty<給与支払行>(), Array.Empty<退職支払行>(), Array.Empty<報酬支払行>(),
            Array.Empty<不動産支払行>(), Array.Empty<不動産支払行>(), Array.Empty<不動産支払行>());
        var h = new 法定調書提出者情報 { 年 = "7" };

        byte[] a = report.CreatePdf(empty, h);
        byte[] b = report.CreatePdf(empty, h);
        Assert.True(a.Length > 10_000);
        Assert.True(b.Length > 10_000);
    }
}
