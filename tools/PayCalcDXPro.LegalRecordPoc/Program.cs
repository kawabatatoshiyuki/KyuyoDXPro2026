using System.Text;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using PayCalcDXPro.Reporting;
using PayCalcDXPro.Reporting.Models;

// 法定調書合計表（方式①）の実機検証：
//   xlsxマスター →(座標抽出) + (背景PDF生成：開発時のみLibreOffice) →(PdfSharpオーバーレイ：実行時純.NET)。
Console.OutputEncoding = Encoding.UTF8;

string template = args.Length > 0
    ? args[0]
    : @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\帳票印刷\テンプレート\R_合計表.xlsx";

string outDir = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outDir);

// --- サンプルデータ ---
var summary = new LegalRecordSummaryCalculator().Calculate(
    給与: new[]
    {
        new 給与支払行 { 給与賞与計 = 5_000_000m, 源泉計 = 120_000m, 源泉徴収票提出 = true },
        new 給与支払行 { 給与賞与計 = 3_500_000m, 前社収入 = 800_000m, 源泉計 = 60_000m, 前社源泉 = 15_000m, 源泉徴収票提出 = true },
        new 給与支払行 { 給与賞与計 = 2_000_000m, 源泉計 = 0m },
    },
    退職: new[] { new 退職支払行 { 提出 = true, 支払金額1 = 3_000_000m, 源泉徴収税額1 = 80_000m } },
    報酬: new[]
    {
        new 報酬支払行 { 号 = 2, 受者区分 = 支払受者区分.個人, 提出 = true,
            支払金額 = new[] { 1_000_000m }, 源泉徴収税額 = new[] { 102_100m } },
    },
    不動産: new[] { new 不動産支払行 { 提出要 = true, 支払金額 = new[] { 1_200_000m } } },
    譲受: Array.Empty<不動産支払行>(),
    あっせん: Array.Empty<不動産支払行>());

var 提出者 = new 法定調書提出者情報
{
    年 = Wareki.年数(2025), 署番号 = "00123", 税務署名 = "麹町", 事業種目 = "ソフトウェア業", 整理番号 = "A-001",
    所在地 = "東京都千代田区一番町1-1", 電話番号 = "03-1234-5678",
    会社名 = "周豊システム株式会社", 会社名カナ = "シュウホウシステム",
    代表者名 = "周豊太郎", 代表者カナ = "シュウホウタロウ",
    事業所番号 = "123456789012", 番号区分 = "2",
};

var xlsxGen = new LegalRecordSummaryXlsxGenerator();

// 1) xlsxマスターから座標レイアウトを自動抽出
IReadOnlyDictionary<string, PdfOverlayField> layout;
using (var fs = File.OpenRead(template)) layout = XlsxOverlayLayoutExtractor.Extract(fs);
Console.WriteLine($"[座標抽出] {layout.Count} 項目");
foreach (var key in new[] { "**署番号", "**1.給与所得の源泉徴収票合計表_俸給､給与､賞与等の総額_支払金額" })
    if (layout.TryGetValue(key, out var f))
        Console.WriteLine($"   {key} → X{f.XMm:F1} Y{f.YMm:F1} W{f.幅Mm:F1}mm {f.揃え} {f.フォントPt:F1}pt");

// 2) 背景PDF（マーカー消去xlsx → LibreOffice：開発時のみ）
string? soffice = LibreOfficePdfConverter.FindDefault();
if (soffice is null) { Console.WriteLine("LibreOffice未検出のため背景PDFを生成できません。"); return; }

byte[] bgXlsx;
using (var fs = File.OpenRead(template)) bgXlsx = xlsxGen.ClearMarkers(fs);
string bgXlsxPath = Path.Combine(outDir, "背景.xlsx");
File.WriteAllBytes(bgXlsxPath, bgXlsx);
string bgPdf = new LibreOfficePdfConverter(soffice).ConvertToPdf(bgXlsxPath, outDir);
Console.WriteLine($"[背景PDF] {bgPdf}（{new FileInfo(bgPdf).Length:N0} bytes）");

// 3) PdfSharpで値をオーバーレイ（実行時相当・純.NET）
byte[] finalPdf;
using (var bg = File.OpenRead(bgPdf))
    finalPdf = new LegalRecordSummaryPdfGenerator().Generate(bg, layout, summary, 提出者);
string finalPath = Path.Combine(outDir, "法定調書合計表_方式1.pdf");
File.WriteAllBytes(finalPath, finalPdf);
Console.WriteLine($"[完成PDF] {finalPath}（{finalPdf.Length:N0} bytes）");

// 4) 目視用にPNG化（検証目的のみ）
string pngDir = Path.Combine(outDir, "png");
var conv = new LibreOfficePdfConverter(soffice);
try
{
    // soffice はPDF→PNGも可能（Drawで読み込み）
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = soffice,
        Arguments = $"--headless --norestore --convert-to png --outdir \"{pngDir}\" \"{finalPath}\"",
        UseShellExecute = false,
    })!.WaitForExit();
    Console.WriteLine($"[PNG] {pngDir}");
}
catch { /* PNGは任意 */ }
