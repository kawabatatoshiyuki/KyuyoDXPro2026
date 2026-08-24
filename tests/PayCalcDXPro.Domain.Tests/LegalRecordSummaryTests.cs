using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>
/// 法定調書合計表の集計（現行 R_合計表）の検証。6区分の総額・提出分・前社分の扱いを確認する。
/// </summary>
public class LegalRecordSummaryTests
{
    private static readonly LegalRecordSummaryCalculator Calc = new();

    private static LegalRecordSummary 集計(
        IReadOnlyList<給与支払行>? 給与 = null,
        IReadOnlyList<退職支払行>? 退職 = null,
        IReadOnlyList<報酬支払行>? 報酬 = null,
        IReadOnlyList<不動産支払行>? 不動産 = null,
        IReadOnlyList<不動産支払行>? 譲受 = null,
        IReadOnlyList<不動産支払行>? あっせん = null)
        => Calc.Calculate(
            給与 ?? Array.Empty<給与支払行>(),
            退職 ?? Array.Empty<退職支払行>(),
            報酬 ?? Array.Empty<報酬支払行>(),
            不動産 ?? Array.Empty<不動産支払行>(),
            譲受 ?? Array.Empty<不動産支払行>(),
            あっせん ?? Array.Empty<不動産支払行>());

    [Fact]
    public void 給与_総額は前社収入と前社源泉を差し引く()
    {
        var 給与 = new[]
        {
            new 給与支払行 { 給与賞与計 = 5_000_000m, 源泉計 = 100_000m, 源泉徴収票提出 = true },
            // 中途就職：前社収入100万・前社源泉2万を含むため差し引く
            new 給与支払行 { 給与賞与計 = 4_000_000m, 前社収入 = 1_000_000m, 源泉計 = 80_000m, 前社源泉 = 20_000m },
        };

        LegalRecordSummary s = 集計(給与: 給与);

        Assert.Equal(2, s.給与_総額.人員);
        Assert.Equal(8_000_000m, s.給与_総額.支払金額);   // 900万 − 100万
        Assert.Equal(160_000m, s.給与_総額.源泉徴収税額); // (10万+8万) − 2万
    }

    [Fact]
    public void 給与_乙欄は年調年税額_その他は源泉計を税額に使う()
    {
        var 給与 = new[]
        {
            new 給与支払行 { 給与賞与計 = 3_000_000m, 年調 = true, 年調年税額 = 50_000m, 源泉計 = 999m },
            new 給与支払行 { 給与賞与計 = 2_000_000m, 年調 = false, 源泉計 = 30_000m },
        };

        LegalRecordSummary s = 集計(給与: 給与);
        Assert.Equal(80_000m, s.給与_総額.源泉徴収税額); // 年調5万 + 源泉計3万
    }

    [Fact]
    public void 給与_源泉徴収税額のない者を数える()
    {
        var 給与 = new[]
        {
            new 給与支払行 { 給与賞与計 = 1_000_000m, 源泉計 = 0m },             // なし
            new 給与支払行 { 給与賞与計 = 3_000_000m, 年調 = true, 年調年税額 = 0m }, // なし
            new 給与支払行 { 給与賞与計 = 4_000_000m, 源泉計 = 50_000m },          // あり
        };

        LegalRecordSummary s = 集計(給与: 給与);
        Assert.Equal(2, s.給与_源泉徴収税額のない者);
    }

    [Fact]
    public void 給与_提出分は前社収入を差し引かず提出者のみ集計する()
    {
        var 給与 = new[]
        {
            new 給与支払行 { 給与賞与計 = 5_000_000m, 前社収入 = 500_000m, 源泉計 = 100_000m, 源泉徴収票提出 = true },
            new 給与支払行 { 給与賞与計 = 4_000_000m, 源泉計 = 80_000m, 源泉徴収票提出 = false },
        };

        LegalRecordSummary s = 集計(給与: 給与);
        Assert.Equal(1, s.給与_提出.人員);
        Assert.Equal(5_000_000m, s.給与_提出.支払金額);   // 前社収入を引かない
        Assert.Equal(100_000m, s.給与_提出.源泉徴収税額);
    }

    [Fact]
    public void 退職_総額と提出分を集計する()
    {
        var 退職 = new[]
        {
            new 退職支払行 { 提出 = true, 支払金額1 = 1_000_000m, 支払金額2 = 500_000m, 源泉徴収税額1 = 30_000m },
            new 退職支払行 { 提出 = false, 支払金額3 = 2_000_000m, 源泉徴収税額3 = 40_000m },
        };

        LegalRecordSummary s = 集計(退職: 退職);
        Assert.Equal(2, s.退職_総額.人員);
        Assert.Equal(3_500_000m, s.退職_総額.支払金額);
        Assert.Equal(70_000m, s.退職_総額.源泉徴収税額);

        Assert.Equal(1, s.退職_提出.人員);
        Assert.Equal(1_500_000m, s.退職_提出.支払金額);
        Assert.Equal(30_000m, s.退職_提出.源泉徴収税額);
    }

    [Fact]
    public void 報酬_号別に個人法人の人員と金額を集計する()
    {
        var 報酬 = new[]
        {
            new 報酬支払行 { 号 = 1, 受者区分 = 支払受者区分.個人, 提出 = true,
                支払金額 = new[] { 200_000m, 100_000m }, 源泉徴収税額 = new[] { 30_000m } },
            new 報酬支払行 { 号 = 1, 受者区分 = 支払受者区分.法人,
                支払金額 = new[] { 500_000m }, 源泉徴収税額 = new[] { 50_000m } },
            new 報酬支払行 { 号 = 2, 受者区分 = 支払受者区分.個人, 提出 = true,
                支払金額 = new[] { 1_000_000m }, 源泉徴収税額 = new[] { 100_000m } },
        };

        LegalRecordSummary s = 集計(報酬: 報酬);

        報酬号別集計 一号 = s.報酬_号別[0];
        Assert.Equal(1, 一号.号);
        Assert.Equal(1, 一号.個人人員);
        Assert.Equal(1, 一号.法人人員);
        Assert.Equal(800_000m, 一号.支払金額);     // (20万+10万)+50万
        Assert.Equal(80_000m, 一号.源泉徴収税額);  // 3万+5万

        // A計
        Assert.Equal(2, s.報酬_A計.個人人員);
        Assert.Equal(1, s.報酬_A計.法人人員);
        Assert.Equal(1_800_000m, s.報酬_A計.支払金額);
        Assert.Equal(180_000m, s.報酬_A計.源泉徴収税額);

        // 提出分（提出=trueの個人2件）
        Assert.Equal(2, s.報酬_提出.個人人員);
        Assert.Equal(0, s.報酬_提出.法人人員);
        Assert.Equal(1_300_000m, s.報酬_提出.支払金額);
        Assert.Equal(130_000m, s.報酬_提出.源泉徴収税額);
    }

    [Fact]
    public void 報酬_号別は8区分すべて返す()
    {
        LegalRecordSummary s = 集計(報酬: Array.Empty<報酬支払行>());
        Assert.Equal(8, s.報酬_号別.Count);
        Assert.All(s.報酬_号別, g => Assert.Equal(0m, g.支払金額));
    }

    [Fact]
    public void 不動産系は総額と提出分を集計し源泉は0()
    {
        var 不動産 = new[]
        {
            new 不動産支払行 { 提出要 = true, 支払金額 = new[] { 600_000m, 200_000m } },
            new 不動産支払行 { 提出要 = false, 支払金額 = new[] { 300_000m } },
        };
        var 譲受 = new[] { new 不動産支払行 { 提出要 = true, 支払金額 = new[] { 1_000_000m, 500_000m, 100_000m } } };
        var あっせん = new[] { new 不動産支払行 { 提出要 = false, 支払金額 = new[] { 80_000m, 20_000m } } };

        LegalRecordSummary s = 集計(不動産: 不動産, 譲受: 譲受, あっせん: あっせん);

        Assert.Equal(2, s.不動産_総額.人員);
        Assert.Equal(1_100_000m, s.不動産_総額.支払金額);
        Assert.Equal(0m, s.不動産_総額.源泉徴収税額);
        Assert.Equal(1, s.不動産_提出.人員);
        Assert.Equal(800_000m, s.不動産_提出.支払金額);

        Assert.Equal(1_600_000m, s.譲受_総額.支払金額);
        Assert.Equal(1_600_000m, s.譲受_提出.支払金額);

        Assert.Equal(100_000m, s.あっせん_総額.支払金額);
        Assert.Equal(0, s.あっせん_提出.人員); // 提出要=false
        Assert.Equal(0m, s.あっせん_提出.支払金額);
    }

    [Fact]
    public void 空入力でも全区分が0で返る()
    {
        LegalRecordSummary s = 集計();
        Assert.Equal(0, s.給与_総額.人員);
        Assert.Equal(0m, s.退職_総額.支払金額);
        Assert.Equal(8, s.報酬_号別.Count);
        Assert.Equal(0m, s.報酬_A計.支払金額);
        Assert.Equal(0, s.不動産_提出.人員);
    }
}
