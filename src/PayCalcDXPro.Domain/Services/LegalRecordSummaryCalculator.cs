using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 給与所得の源泉徴収票等の法定調書合計表の集計（現行 R_合計表 のロジックに一致）。
/// 6区分（給与所得・退職所得・報酬料金・不動産使用料・不動産譲受・不動産あっせん）について、
/// 全体の人員・支払金額・源泉徴収税額と、支払調書を提出するものの分を集計する。
/// 描画（官製様式）は本集計結果を様式へ流し込む別レイヤの責務。
/// </summary>
public sealed class LegalRecordSummaryCalculator
{
    public LegalRecordSummary Calculate(
        IReadOnlyList<給与支払行> 給与,
        IReadOnlyList<退職支払行> 退職,
        IReadOnlyList<報酬支払行> 報酬,
        IReadOnlyList<不動産支払行> 不動産,
        IReadOnlyList<不動産支払行> 譲受,
        IReadOnlyList<不動産支払行> あっせん)
    {
        return new LegalRecordSummary
        {
            給与_総額 = 給与総額を集計(給与),
            給与_源泉徴収税額のない者 = 給与.Count(g => g.採用税額 == 0m),
            給与_提出 = 給与提出を集計(給与),

            退職_総額 = 退職を集計(退職),
            退職_提出 = 退職を集計(退職.Where(t => t.提出).ToList()),

            報酬_号別 = 報酬を号別集計(報酬),
            報酬_A計 = 報酬A計(報酬),
            報酬_提出 = 報酬提出を集計(報酬),

            不動産_総額 = 不動産を集計(不動産),
            不動産_提出 = 不動産を集計(不動産.Where(f => f.提出要).ToList()),

            譲受_総額 = 不動産を集計(譲受),
            譲受_提出 = 不動産を集計(譲受.Where(f => f.提出要).ToList()),

            あっせん_総額 = 不動産を集計(あっせん),
            あっせん_提出 = 不動産を集計(あっせん.Where(f => f.提出要).ToList()),
        };
    }

    // ---- 1. 給与所得 ----

    private static 区分集計 給与総額を集計(IReadOnlyList<給与支払行> 給与)
    {
        // 支払金額 = Σ給与賞与計 − Σ前社収入
        decimal 支払 = 給与.Sum(g => g.給与賞与計) - 給与.Sum(g => g.前社収入);
        // 源泉徴収税額 = Σ採用税額 − Σ(前社源泉>0 のみ)。中途就職者の前職源泉分を除外する。
        decimal 税額 = 給与.Sum(g => g.採用税額) - 給与.Where(g => g.前社源泉 > 0m).Sum(g => g.前社源泉);
        return new 区分集計 { 人員 = 給与.Count, 支払金額 = 支払, 源泉徴収税額 = 税額 };
    }

    private static 区分集計 給与提出を集計(IReadOnlyList<給与支払行> 給与)
    {
        var 提出 = 給与.Where(g => g.源泉徴収票提出).ToList();
        // 提出分の支払金額は前社収入を差し引かない（現行どおり）。
        return new 区分集計
        {
            人員 = 提出.Count,
            支払金額 = 提出.Sum(g => g.給与賞与計),
            源泉徴収税額 = 提出.Sum(g => g.採用税額),
        };
    }

    // ---- 2. 退職所得 ----

    private static 区分集計 退職を集計(IReadOnlyList<退職支払行> 退職) => new()
    {
        人員 = 退職.Count,
        支払金額 = 退職.Sum(t => t.支払金額計),
        源泉徴収税額 = 退職.Sum(t => t.源泉徴収税額計),
    };

    // ---- 3. 報酬料金 ----

    private static IReadOnlyList<報酬号別集計> 報酬を号別集計(IReadOnlyList<報酬支払行> 報酬)
    {
        var result = new List<報酬号別集計>();
        for (int 号 = 1; 号 <= 8; 号++)
        {
            var rows = 報酬.Where(r => r.号 == 号).ToList();
            result.Add(new 報酬号別集計
            {
                号 = 号,
                個人人員 = rows.Count(r => r.受者区分 == 支払受者区分.個人),
                法人人員 = rows.Count(r => r.受者区分 == 支払受者区分.法人),
                支払金額 = rows.Sum(r => r.支払金額計),
                源泉徴収税額 = rows.Sum(r => r.源泉徴収税額計),
            });
        }
        return result;
    }

    private static 報酬合計集計 報酬A計(IReadOnlyList<報酬支払行> 報酬)
    {
        // A計は1〜8号該当のみを対象に合計する。
        var 該当 = 報酬.Where(r => r.号 is >= 1 and <= 8).ToList();
        return new 報酬合計集計
        {
            個人人員 = 該当.Count(r => r.受者区分 == 支払受者区分.個人),
            法人人員 = 該当.Count(r => r.受者区分 == 支払受者区分.法人),
            支払金額 = 該当.Sum(r => r.支払金額計),
            源泉徴収税額 = 該当.Sum(r => r.源泉徴収税額計),
        };
    }

    private static 報酬合計集計 報酬提出を集計(IReadOnlyList<報酬支払行> 報酬)
    {
        var 提出 = 報酬.Where(r => r.提出).ToList();
        return new 報酬合計集計
        {
            個人人員 = 提出.Count(r => r.受者区分 == 支払受者区分.個人),
            法人人員 = 提出.Count(r => r.受者区分 == 支払受者区分.法人),
            支払金額 = 提出.Sum(r => r.支払金額計),
            源泉徴収税額 = 提出.Sum(r => r.源泉徴収税額計),
        };
    }

    // ---- 4〜6. 不動産系（使用料・譲受・あっせん共通） ----

    private static 区分集計 不動産を集計(IReadOnlyList<不動産支払行> 行) => new()
    {
        人員 = 行.Count,
        支払金額 = 行.Sum(f => f.支払金額計),
        源泉徴収税額 = 0m, // 不動産系は源泉徴収なし
    };
}
