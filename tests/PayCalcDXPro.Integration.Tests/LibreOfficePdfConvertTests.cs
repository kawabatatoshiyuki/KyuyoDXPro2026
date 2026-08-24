using System.Text;
using System.Text.RegularExpressions;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using PayCalcDXPro.Reporting;
using PayCalcDXPro.Reporting.Models;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 帳票方式Aの最終段（xlsx→PDF）の実機検証。現行テンプレ R_合計表.xlsx へ流し込み、
/// LibreOffice headless で PDF 化して A4（210×297mm）で出力されることを確認する。
/// テンプレ または LibreOffice が無い環境では自動スキップ。
/// </summary>
public class LibreOfficePdfConvertTests : IDisposable
{
    private static readonly string TemplatePath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\帳票印刷\テンプレート\R_合計表.xlsx";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_lo_{Guid.NewGuid():N}");

    [SkippableFact]
    public void 法定調書合計表がA4のPDFに変換される()
    {
        Skip.IfNot(File.Exists(TemplatePath), "現行テンプレ R_合計表.xlsx が無いためスキップ");
        string? soffice = LibreOfficePdfConverter.FindDefault();
        Skip.If(soffice is null, "LibreOffice(soffice.com) が無いためスキップ");

        Directory.CreateDirectory(_dir);

        LegalRecordSummary summary = new LegalRecordSummaryCalculator().Calculate(
            給与: new[] { new 給与支払行 { 給与賞与計 = 5_000_000m, 源泉計 = 120_000m, 源泉徴収票提出 = true } },
            退職: Array.Empty<退職支払行>(),
            報酬: Array.Empty<報酬支払行>(),
            不動産: Array.Empty<不動産支払行>(),
            譲受: Array.Empty<不動産支払行>(),
            あっせん: Array.Empty<不動産支払行>());

        var 提出者 = new 法定調書提出者情報 { 年 = "7", 署番号 = "00123", 会社名 = "周豊システム株式会社" };

        byte[] xlsx;
        using (var fs = File.OpenRead(TemplatePath))
            xlsx = new LegalRecordSummaryXlsxGenerator().Generate(fs, summary, 提出者);

        string xlsxPath = Path.Combine(_dir, "法定調書合計表.xlsx");
        File.WriteAllBytes(xlsxPath, xlsx);

        string pdf = new LibreOfficePdfConverter(soffice!).ConvertToPdf(xlsxPath, _dir);

        Assert.True(File.Exists(pdf));
        Assert.True(new FileInfo(pdf).Length > 1000, "PDFサイズが小さすぎます");

        (double w, double h)? size = ReadPageSizeMm(pdf);
        Assert.NotNull(size);
        Assert.Equal(210.0, size!.Value.w, 1.0); // A4 幅
        Assert.Equal(297.0, size.Value.h, 1.0);  // A4 高さ
    }

    private static (double w, double h)? ReadPageSizeMm(string pdfPath)
    {
        string text = Encoding.Latin1.GetString(File.ReadAllBytes(pdfPath));
        Match m = Regex.Match(text, @"/MediaBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\]");
        if (!m.Success) return null;
        const double mmPerPt = 25.4 / 72.0;
        return (double.Parse(m.Groups[3].Value) * mmPerPt, double.Parse(m.Groups[4].Value) * mmPerPt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* 一時フォルダ削除失敗は無視 */ }
        }
    }
}
