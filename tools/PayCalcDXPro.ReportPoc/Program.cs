using ClosedXML.Excel;

// 手順2：官製様式（源泉徴収票）の寸法精度検証。
// 既存xlsxテンプレートを ClosedXML で開いて構造を解析し、サンプル値を流し込んで保存する（Approach A）。
Console.OutputEncoding = System.Text.Encoding.UTF8;

string template = args.Length > 0
    ? args[0]
    : @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\帳票印刷\テンプレート\R_源泉徴収票.xlsx";

var outDir = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outDir);

using var wb = new XLWorkbook(template);

Console.WriteLine("=== テンプレート構造 ===");
Console.WriteLine($"ファイル: {template}");
Console.WriteLine($"シート数: {wb.Worksheets.Count}");

foreach (var ws in wb.Worksheets)
{
    var used = ws.RangeUsed();
    Console.WriteLine($"\n[シート] {ws.Name}");
    Console.WriteLine($"  使用範囲: {(used is null ? "(空)" : used.RangeAddress.ToStringRelative())}");
    var ps = ws.PageSetup;
    Console.WriteLine($"  用紙: {ps.PaperSize}  向き: {ps.PageOrientation}");
    Console.WriteLine($"  余白(in) 上{ps.Margins.Top} 下{ps.Margins.Bottom} 左{ps.Margins.Left} 右{ps.Margins.Right}");
    Console.WriteLine($"  印刷範囲: {(ps.PrintAreas.Any() ? string.Join(",", ps.PrintAreas.Select(a => a.RangeAddress.ToStringRelative())) : "(未設定)")}");
    Console.WriteLine($"  拡大縮小: {ps.Scale}%  横{ps.PagesWide}x縦{ps.PagesTall}");
}

// 先頭シートにサンプル値を流し込み（実際の項目セルは現行 R_源泉徴収票.cs の対応に合わせて確定する）
var sheet = wb.Worksheets.First();
var probe = new (string cell, string value)[]
{
    ("A1", "令和6年分 給与所得の源泉徴収票（PoC流し込み）"),
};
foreach (var (cell, value) in probe)
{
    try { sheet.Cell(cell).Value = value; } catch { /* セル位置はテンプレ依存。PoCでは握りつぶす */ }
}

string filled = Path.Combine(outDir, "源泉徴収票_filled.xlsx");
wb.SaveAs(filled);
Console.WriteLine($"\n[Approach A] 流し込み済みxlsxを保存: {filled}");
Console.WriteLine("→ Excel/LibreOffice で同xlsxを既定レイアウトのままPDF化（寸法はテンプレ準拠で保証）。");

// Approach B: QuestPDF で mm精度のフォーム描画
byte[] pdfB = new PayCalcDXPro.Reporting.FormLayoutPdfGenerator().Generate();
string approachB = Path.Combine(outDir, "源泉徴収票_ApproachB_QuestPDF.pdf");
File.WriteAllBytes(approachB, pdfB);
Console.WriteLine($"\n[Approach B] mm精度PDFを保存: {approachB}（{pdfB.Length:N0} bytes）");
