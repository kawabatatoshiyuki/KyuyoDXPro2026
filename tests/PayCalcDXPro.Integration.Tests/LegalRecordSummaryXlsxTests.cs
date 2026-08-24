using ClosedXML.Excel;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using PayCalcDXPro.Reporting;
using PayCalcDXPro.Reporting.Models;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 法定調書合計表の官製様式生成（帳票方式A：xlsxテンプレ流し込み）の検証。
/// ルーティング（マーカー→集計値）の単体確認、合成xlsxへの流し込み、
/// および現行テンプレR_合計表.xlsxの全89マーカーが解決されること（テンプレ存在時のみ）。
/// </summary>
public class LegalRecordSummaryXlsxTests
{
    private static readonly string TemplatePath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\帳票印刷\テンプレート\R_合計表.xlsx";

    private const string 番号13 = "1234567890123";

    private static LegalRecordSummary サンプル集計()
    {
        var calc = new LegalRecordSummaryCalculator();
        return calc.Calculate(
            給与: new[]
            {
                new 給与支払行 { 給与賞与計 = 5_000_000m, 源泉計 = 100_000m, 源泉徴収票提出 = true },
                new 給与支払行 { 給与賞与計 = 3_000_000m, 源泉計 = 0m },
            },
            退職: new[] { new 退職支払行 { 提出 = true, 支払金額1 = 2_000_000m, 源泉徴収税額1 = 50_000m } },
            報酬: new[]
            {
                new 報酬支払行 { 号 = 1, 受者区分 = 支払受者区分.個人, 提出 = true,
                    支払金額 = new[] { 300_000m }, 源泉徴収税額 = new[] { 30_000m } },
            },
            不動産: new[] { new 不動産支払行 { 提出要 = true, 支払金額 = new[] { 600_000m } } },
            譲受: Array.Empty<不動産支払行>(),
            あっせん: Array.Empty<不動産支払行>());
    }

    private static 法定調書提出者情報 サンプル提出者() => new()
    {
        年 = "令和7",
        署番号 = "00123",
        税務署名 = "麹町",
        事業種目 = "ソフトウェア業",
        整理番号 = "A-1",
        所在地 = "東京都千代田区…",
        電話番号 = "03-0000-0000",
        会社名 = "周豊システム株式会社",
        会社名カナ = "シュウホウシステム",
        代表者名 = "周豊太郎",
        代表者カナ = "シュウホウタロウ",
        事業所番号 = "123456789012", // 個人12桁
        番号区分 = "2",
    };

    [Fact]
    public void ルーティングは各区分のマーカーを集計値へ解決する()
    {
        LegalRecordSummary s = サンプル集計();
        法定調書提出者情報 h = サンプル提出者();

        Assert.True(LegalRecordSummaryXlsxGenerator.TryResolve(
            "**1.給与所得の源泉徴収票合計表_俸給､給与､賞与等の総額_人員", s, h, 番号13, out object? 人員));
        Assert.Equal(2, 人員);

        Assert.True(LegalRecordSummaryXlsxGenerator.TryResolve(
            "**1.給与所得の源泉徴収票合計表_俸給､給与､賞与等の総額_支払金額", s, h, 番号13, out object? 支払));
        Assert.Equal(8_000_000L, 支払);

        // 1号該当・支払金額（記述文字は問わずパターンで解決）
        Assert.True(LegalRecordSummaryXlsxGenerator.TryResolve(
            "**3.報酬、料金、契約金及び賞金の支払調書合計表_原稿料(１号該当)_支払金額", s, h, 番号13, out object? 号1));
        Assert.Equal(300_000L, 号1);

        // ヘッダ
        Assert.True(LegalRecordSummaryXlsxGenerator.TryResolve("**署番号", s, h, 番号13, out object? sbn));
        Assert.Equal("00123", sbn);

        // 未対応マーカーは matched=false
        Assert.False(LegalRecordSummaryXlsxGenerator.TryResolve("**存在しない項目", s, h, 番号13, out _));
    }

    [Fact]
    public void 源泉徴収税額のない者は人数を返す()
    {
        LegalRecordSummary s = サンプル集計();
        Assert.True(LegalRecordSummaryXlsxGenerator.TryResolve(
            "**1.給与所得の源泉徴収票合計表_俸給､給与､賞与等の総額_源泉徴収税額のない者",
            s, サンプル提出者(), 番号13, out object? v));
        Assert.Equal(1, v); // 源泉計0の従業員が1人
    }

    [Fact]
    public void 個人番号12桁は先頭スペース付きで13桁に分割される()
    {
        LegalRecordSummary s = サンプル集計();
        法定調書提出者情報 h = サンプル提出者(); // 個人12桁

        string 番号 = " 123456789012"; // 先頭スペース＋12桁
        for (int i = 1; i <= 13; i++)
        {
            Assert.True(LegalRecordSummaryXlsxGenerator.TryResolve(
                $"**提出者_個人番号又は法人番号_{i}番号", s, h, 番号, out object? c));
            Assert.Equal(番号[i - 1].ToString(), c);
        }
    }

    [Fact]
    public void 合成xlsxに流し込み0値は空欄になる()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("s");
        ws.Cell("A1").Value = "**署番号";
        ws.Cell("A2").Value = "**1.給与所得の源泉徴収票合計表_俸給､給与､賞与等の総額_支払金額";
        ws.Cell("A3").Value = "**5.不動産等の譲受けの対価の支払調書合計表_譲受けの対価の総額_支払金額"; // 0→空欄

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            wb.SaveAs(ms);
            bytes = new LegalRecordSummaryXlsxGenerator().Generate(
                new MemoryStream(ms.ToArray()), サンプル集計(), サンプル提出者());
        }

        using var outWb = new XLWorkbook(new MemoryStream(bytes));
        var outWs = outWb.Worksheet(1);
        Assert.Equal("00123", outWs.Cell("A1").GetString());
        Assert.Equal(8_000_000, outWs.Cell("A2").GetValue<long>());
        Assert.True(outWs.Cell("A3").IsEmpty()); // 譲受0件→空欄
    }

    [SkippableFact]
    public void 現行テンプレの全マーカーが解決され流し込み後に残骸が残らない()
    {
        Skip.IfNot(File.Exists(TemplatePath), "現行テンプレ R_合計表.xlsx が見つからないためスキップ");

        LegalRecordSummary s = サンプル集計();
        法定調書提出者情報 h = サンプル提出者();

        // 1) テンプレ内の全プレースホルダがルーティングで解決されること（未対応マーカー検出）
        var unresolved = new List<string>();
        using (var wb = new XLWorkbook(TemplatePath))
        {
            foreach (var ws in wb.Worksheets)
            foreach (var cell in ws.CellsUsed(c => c.DataType == XLDataType.Text && c.GetString().StartsWith("**")))
            {
                if (!LegalRecordSummaryXlsxGenerator.TryResolve(cell.GetString(), s, h, "1234567890123", out _))
                    unresolved.Add(cell.GetString());
            }
        }
        Assert.Empty(unresolved);

        // 2) 生成後はどのセルにも「**」マーカーが残らない
        byte[] bytes;
        using (var fs = File.OpenRead(TemplatePath))
            bytes = new LegalRecordSummaryXlsxGenerator().Generate(fs, s, h);

        using var outWb = new XLWorkbook(new MemoryStream(bytes));
        var leftovers = new List<string>();
        var 値一覧 = new List<string>();
        foreach (var ws in outWb.Worksheets)
        foreach (var cell in ws.CellsUsed(c => c.DataType == XLDataType.Text))
        {
            string t = cell.GetString();
            値一覧.Add(t);
            if (t.StartsWith("**")) leftovers.Add(t);
        }
        Assert.Empty(leftovers);

        // 3) 代表値が流し込まれている
        Assert.Contains("00123", 値一覧);          // 署番号
        Assert.Contains("ソフトウェア業", 値一覧);  // 事業種目
    }
}
