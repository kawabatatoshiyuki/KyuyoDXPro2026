using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Reference;
using PayCalcDXPro.Domain.Services;
using PayCalcDXPro.Reporting;
using PayCalcDXPro.Reporting.Models;

// ───────────────────────────────────────────────────────────────
// PoC ランナー：B（ドメイン計算）→ A（帳票PDF生成）を一気通貫で実行する。
// 現行の WinForms/VBReport なしで「計算 → サーバ側PDF出力」が成立することを確認する。
// ───────────────────────────────────────────────────────────────

Console.OutputEncoding = System.Text.Encoding.UTF8;

// --- B: ドメイン計算（料率・別表は SampleData = 本番はDBから注入） ---
var calculator = new PayrollCalculator(SampleData.Rates, SampleData.TaxTables);

var input = new PayrollInput
{
    課税支給合計 = 300_000m,
    非課税支給合計 = 10_000m,   // 非課税通勤費
    標準報酬月額 = 300_000m,
    扶養親族等の数 = 1,
    介護保険対象 = false,
    その他控除合計 = 8_000m,    // 住民税 等
};

PayrollResult result = calculator.Calculate(input);

Console.WriteLine("=== B: 給与計算結果 ===");
Console.WriteLine($"総支給額    : {result.総支給額,12:N0}");
Console.WriteLine($"  健康保険  : {result.社会保険.健康保険,12:N0}");
Console.WriteLine($"  厚生年金  : {result.社会保険.厚生年金,12:N0}");
Console.WriteLine($"  雇用保険  : {result.社会保険.雇用保険,12:N0}");
Console.WriteLine($"  所得税    : {result.所得税,12:N0}");
Console.WriteLine($"  その他控除: {result.その他控除合計,12:N0}");
Console.WriteLine($"控除合計    : {result.控除合計,12:N0}");
Console.WriteLine($"差引支給額  : {result.差引支給額,12:N0}");

// --- A: 帳票PDF生成 ---
var model = new PaySlipModel
{
    会社名 = "株式会社サンプル商事",
    従業員名 = "山田 太郎",
    支給年月 = "令和6年6月",
    支給内訳 = new[]
    {
        new LineItem("基本給", 280_000m),
        new LineItem("課税通勤費", 20_000m),
        new LineItem("非課税通勤費", 10_000m),
    },
    計算結果 = result,
};

byte[] pdf = new PaySlipPdfGenerator().Generate(model);

var outDir = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outDir);
var outPath = Path.Combine(outDir, "給与明細_山田太郎_令和6年6月.pdf");
File.WriteAllBytes(outPath, pdf);

Console.WriteLine();
Console.WriteLine("=== A: 帳票PDF生成 ===");
Console.WriteLine($"出力: {outPath}");
Console.WriteLine($"サイズ: {pdf.Length:N0} bytes");
