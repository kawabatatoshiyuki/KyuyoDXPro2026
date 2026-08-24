using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 特定親族特別控除を計算する（2025年改正・現行 F_年調_従業員情報 の tokubetuKoujo に一致）。
/// 対象は19歳以上23歳未満の親族。収入種類により判定方法が分かれる：
///   ・所得     … 合計所得金額が 58万円超〜123万円以下 のとき、合計所得の区間で控除額を引く
///   ・給与/年金 … 収入金額が 123万円超〜188万円以下 のとき、収入金額の区間で控除額を引く
///   ・上記の範囲外・その他は控除0。
/// 区間判定は現行SQL（自 &lt; 金額 AND 至 ≧ 金額）に合わせ「下限は排他・上限は包含」。
/// </summary>
public sealed class SpecialRelativeDeductionCalculator
{
    // 現行 MConst と同値
    private const decimal 所得下限 = 580_000m;     // TOKUTEI_SHOTOKU_MIN（58万円超）
    private const decimal 所得上限 = 1_230_000m;   // TOKUTEI_SHOTOKU_MAX（123万円以下）
    private const decimal 収入下限 = 1_230_000m;   // TOKUTEI_KYUYO_MIN（123万円超）
    private const decimal 収入上限 = 1_880_000m;   // TOKUTEI_KYUYO_MAX（188万円以下）

    private readonly SpecialRelativeDeductionTable _t;

    public SpecialRelativeDeductionCalculator(SpecialRelativeDeductionTable table) => _t = table;

    /// <summary>特定親族1人分の控除額（要件を満たさなければ0）。</summary>
    public decimal 控除額1人(特定親族 親族, int 判定年)
    {
        // 年齢要件：19歳以上23歳未満
        if (!AgeClassifier.は19歳以上23歳未満(親族.生年月日, 判定年)) return 0m;

        return 親族.収入種類 switch
        {
            収入種類.所得 => 所得から控除額(親族.金額),
            収入種類.給与 or 収入種類.年金 => 収入から控除額(親族.金額),
            _ => 0m,
        };
    }

    /// <summary>特定親族全員分の控除額合計（現行 shinzKojoTotal）。</summary>
    public decimal 控除合計(IEnumerable<特定親族> 親族たち, int 判定年)
        => 親族たち.Sum(p => 控除額1人(p, 判定年));

    // ---- 内部 ----

    private decimal 所得から控除額(decimal 合計所得)
    {
        // 58万円超〜123万円以下のみ対象
        if (合計所得 <= 所得下限 || 合計所得 > 所得上限) return 0m;
        return _t.行.FirstOrDefault(r => r.合計所得下限 < 合計所得 && 合計所得 <= r.合計所得上限)?.控除額 ?? 0m;
    }

    private decimal 収入から控除額(decimal 収入)
    {
        // 123万円超〜188万円以下のみ対象
        if (収入 <= 収入下限 || 収入 > 収入上限) return 0m;
        return _t.行.FirstOrDefault(r => r.収入下限 < 収入 && 収入 <= r.収入上限)?.控除額 ?? 0m;
    }
}
