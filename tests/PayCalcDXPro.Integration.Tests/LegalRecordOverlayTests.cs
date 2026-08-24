using ClosedXML.Excel;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using PayCalcDXPro.Reporting;
using PayCalcDXPro.Reporting.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 方式①（xlsxマスターから座標自動抽出 → PdfSharpで背景PDFへ値をオーバーレイ、実行時は純.NET）の検証。
/// </summary>
public class LegalRecordOverlayTests
{
    private static readonly string TemplatePath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\帳票印刷\テンプレート\R_合計表.xlsx";

    // --- 座標抽出 ---

    [Fact]
    public void 座標抽出は列で右へ_行で下へ_結合で幅が広がる()
    {
        byte[] xlsx;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("s");
            ws.Column(1).Width = 10;
            ws.Column(2).Width = 10;
            ws.Column(3).Width = 10;
            ws.Cell("A1").Value = "**a";          // 左上
            ws.Cell("C3").Value = "**b";          // 右下
            ws.Range("A5:C5").Merge();
            ws.Cell("A5").Value = "**m";          // 3列結合
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            xlsx = ms.ToArray();
        }

        var layout = XlsxOverlayLayoutExtractor.Extract(new MemoryStream(xlsx));

        Assert.Equal(3, layout.Count);
        Assert.True(layout["**b"].XMm > layout["**a"].XMm, "列が右なら X が大きい");
        Assert.True(layout["**b"].YMm > layout["**a"].YMm, "行が下なら Y が大きい");
        Assert.True(layout["**m"].幅Mm > layout["**a"].幅Mm, "結合セルは幅が広い");
    }

    // --- オーバーレイ（背景PDFはPdfSharpで合成、LibreOffice不要） ---

    private static byte[] BlankA4()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void オーバーレイは日本語値を描画しA4のPDFを返す()
    {
        var layout = new Dictionary<string, PdfOverlayField>
        {
            ["**署番号"] = new() { XMm = 150, YMm = 18, 幅Mm = 30, 高Mm = 6, フォントPt = 10 },
            ["**提出者_氏名又は名称"] = new() { XMm = 20, YMm = 40, 幅Mm = 60, 高Mm = 6, フォントPt = 10 },
            ["**1.給与所得の源泉徴収票合計表_俸給､給与､賞与等の総額_支払金額"] =
                new() { XMm = 110, YMm = 80, 幅Mm = 40, 高Mm = 6, フォントPt = 10, 揃え = PdfTextAlign.Right },
        };

        LegalRecordSummary s = new LegalRecordSummaryCalculator().Calculate(
            給与: new[] { new 給与支払行 { 給与賞与計 = 5_000_000m, 源泉計 = 100_000m } },
            退職: Array.Empty<退職支払行>(), 報酬: Array.Empty<報酬支払行>(),
            不動産: Array.Empty<不動産支払行>(), 譲受: Array.Empty<不動産支払行>(), あっせん: Array.Empty<不動産支払行>());
        var h = new 法定調書提出者情報 { 署番号 = "00123", 会社名 = "周豊システム株式会社" };

        byte[] bg = BlankA4();
        byte[] outPdf = new LegalRecordSummaryPdfGenerator().Generate(new MemoryStream(bg), layout, s, h);

        Assert.True(outPdf.Length > bg.Length, "描画により内容が増える");

        using var doc = PdfReader.Open(new MemoryStream(outPdf), PdfDocumentOpenMode.Import);
        Assert.Equal(1, doc.PageCount);
        Assert.Equal(210, doc.Pages[0].Width.Millimeter, 1.0);
        Assert.Equal(297, doc.Pages[0].Height.Millimeter, 1.0);
    }

    [Fact]
    public void 日本語フォントが解決できる()
    {
        JapaneseFontResolver.EnsureRegistered();
        var font = new XFont(JapaneseFontResolver.FamilyName, 10);
        Assert.NotNull(font); // 例外が出ないこと（TTC→単体TTF変換が機能）
    }

    [SkippableFact]
    public void 現行テンプレから全89項目の座標がA4内に抽出される()
    {
        Skip.IfNot(File.Exists(TemplatePath), "現行テンプレ R_合計表.xlsx が無いためスキップ");

        using var fs = File.OpenRead(TemplatePath);
        var layout = XlsxOverlayLayoutExtractor.Extract(fs);

        Assert.Equal(89, layout.Count);
        Assert.All(layout.Values, f =>
        {
            Assert.InRange(f.XMm, 0, 210);
            Assert.InRange(f.YMm, 0, 297);
            Assert.True(f.幅Mm > 0);
        });
    }
}
