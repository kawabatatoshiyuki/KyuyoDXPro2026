using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 扶養控除・障害者控除・寡婦/ひとり親控除・勤労学生控除を計算する（人的控除）。
/// 現行 F_年調_従業員情報 の syogaiayaKoujo / fuyoKoujo に一致。
/// 結果の合計が年末調整の「扶養控除等合計」になる。
/// </summary>
public sealed class DependentDeductionCalculator
{
    // 本人所得しきい値（現行 MConst）
    private const decimal 寡婦ひとり親_所得上限 = 5_000_000m; // KUY_KOJ_MAX
    private const decimal 勤労学生_所得上限 = 850_000m;       // KUY_KOJ_MIN
    private const decimal 障害配偶者_所得上限 = 580_000m;     // HIG_STK_MAX

    private readonly DependentDeductionTable _t;

    public DependentDeductionCalculator(DependentDeductionTable table) => _t = table;

    /// <summary>控除対象扶養親族1人分の控除額（扶養控除＋障害者控除の加算）。</summary>
    public decimal 扶養控除1人(扶養親族 d)
    {
        decimal total = 0m;

        if (d.年齢区分 == 扶養年齢区分.特定扶養) total += _t.特定扶養加算;
        if (d.年齢区分 == 扶養年齢区分.老人) total += d.同居 ? _t.同居老親加算 : _t.老人その他加算;

        if (d.障害 == 障害区分.特別障害) total += d.同居 ? _t.同居特別障害 : _t.特別障害;
        else if (d.障害 == 障害区分.一般障害) total += _t.一般障害;

        // 16歳以上（年少以外）のみ扶養控除の基礎額を加算
        if (d.年齢区分 != 扶養年齢区分.年少) total += _t.一般扶養;

        return total;
    }

    /// <summary>生年月日から年齢区分を判定して控除対象扶養親族1人分の控除額を求める。</summary>
    public decimal 扶養控除1人(DateOnly 生年月日, int 判定年, bool 同居 = false, 障害区分 障害 = 障害区分.なし)
        => 扶養控除1人(new 扶養親族
        {
            年齢区分 = AgeClassifier.扶養区分(生年月日, 判定年),
            同居 = 同居,
            障害 = 障害,
        });

    /// <summary>扶養親族全員の控除額合計。</summary>
    public decimal 扶養控除合計(IEnumerable<扶養親族> 扶養親族たち)
        => 扶養親族たち.Sum(扶養控除1人);

    /// <summary>障害者控除（配偶者分）。配偶者の合計所得58万円以下が要件。</summary>
    public decimal 障害者控除_配偶者(障害区分 配偶者障害, bool 同居, decimal 配偶者合計所得)
    {
        if (配偶者合計所得 > 障害配偶者_所得上限) return 0m;
        return 配偶者障害 switch
        {
            障害区分.特別障害 => 同居 ? _t.同居特別障害 : _t.特別障害,
            障害区分.一般障害 => _t.一般障害,
            _ => 0m,
        };
    }

    /// <summary>所得者本人の控除（障害者・寡婦・ひとり親・勤労学生）。本人合計所得で要件判定。</summary>
    public decimal 本人控除(障害区分 本人障害, 寡婦ひとり親区分 寡婦ひとり親, bool 勤労学生, decimal 本人合計所得)
    {
        decimal total = 0m;

        // 寡婦・ひとり親（合計所得500万円以下）
        if (本人合計所得 <= 寡婦ひとり親_所得上限)
        {
            if (寡婦ひとり親 == 寡婦ひとり親区分.ひとり親) total += _t.ひとり親;
            else if (寡婦ひとり親 == 寡婦ひとり親区分.寡婦) total += _t.一般障害; // 寡婦控除=27万
        }

        // 本人の障害者控除（一般27万・特別40万）
        if (本人障害 == 障害区分.特別障害) total += _t.特別障害;
        else if (本人障害 == 障害区分.一般障害) total += _t.一般障害;

        // 勤労学生（合計所得85万円以下）
        if (勤労学生 && 本人合計所得 <= 勤労学生_所得上限) total += _t.一般障害; // 勤労学生控除=27万

        return total;
    }

    /// <summary>
    /// 人的控除（扶養・障害者・寡婦/ひとり親・勤労学生）の合計。
    /// 年末調整の「扶養控除等合計」として <see cref="YearEndAdjustmentCalculator.CalculateWithDeductions"/> に渡す。
    /// </summary>
    public decimal 人的控除合計(
        IEnumerable<扶養親族> 扶養親族たち,
        障害区分 配偶者障害, bool 配偶者同居, decimal 配偶者合計所得,
        障害区分 本人障害, 寡婦ひとり親区分 寡婦ひとり親, bool 勤労学生, decimal 本人合計所得)
        => 扶養控除合計(扶養親族たち)
         + 障害者控除_配偶者(配偶者障害, 配偶者同居, 配偶者合計所得)
         + 本人控除(本人障害, 寡婦ひとり親, 勤労学生, 本人合計所得);
}
