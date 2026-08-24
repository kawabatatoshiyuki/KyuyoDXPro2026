using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 所得金額調整控除（子ども・特別障害者等）の適用要件を自動判定する。
/// 現行 F_年調_従業員情報 の shotokuTyoseiKoujo / ckTyoSynzoku / chkFuyoSyunyu に一致。
///
/// 給与・賞与の総額が850万円超で、かつ次のいずれかに該当すれば適用：
///   ・本人が特別障害者
///   ・「配偶者が扶養する23歳未満／特別障害の扶養親族を有する」旨の申告（現行 ck_not）
///   ・特別障害である同一生計配偶者（氏名入力済み＋収入要件）
///   ・23歳未満または特別障害の扶養親族（特定親族特別控除を受けておらず＋扶養の収入要件を満たす）
///
/// 判定結果は <see cref="Models.YearEndDeductionInput.所得金額調整控除の適用"/> に渡し、
/// 金額計算は <see cref="YearEndAdjustmentCalculator"/> が行う。
/// </summary>
public sealed class IncomeAdjustmentEligibilityEvaluator
{
    // 現行 MConst と同値
    private const decimal 給与賞与総額閾値 = 8_500_000m; // STK_KOJ_HNT_MAX（850万円超で対象）
    private const decimal 配偶者_給与年金上限 = 1_230_000m; // HGS_GAK_MAX（配偶者は年金も収入で判定）
    private const decimal 配偶者_所得上限 = 580_000m;       // HIG_STK_MAX
    private const decimal 扶養_給与上限 = 1_230_000m;       // FYSZ_GAK_MAX
    private const decimal 扶養_所得上限 = 580_000m;         // FYSZ_STK_MAX

    // 扶養親族の年金収入→所得換算（現行 calNenkinSyotoku／T_速算表）。年金扶養を判定する場合に必須。
    private readonly Func<decimal, DateOnly, decimal>? _年金所得換算;

    public IncomeAdjustmentEligibilityEvaluator(Func<decimal, DateOnly, decimal>? 年金所得換算 = null)
        => _年金所得換算 = 年金所得換算;

    /// <summary>所得金額調整控除を適用するか。</summary>
    public bool 適用するか(
        decimal 給与賞与総額,
        bool 本人特別障害,
        bool 配偶者扶養申告,
        調整控除配偶者? 配偶者,
        IEnumerable<調整控除扶養親族> 扶養親族たち,
        int 判定年)
    {
        if (給与賞与総額 <= 給与賞与総額閾値) return false;
        if (本人特別障害) return true;
        if (配偶者扶養申告) return true;
        if (配偶者 is not null && 配偶者が対象(配偶者)) return true;
        return 扶養親族たち.Any(f => 扶養が対象(f, 判定年));
    }

    // ---- 内部判定 ----

    private bool 配偶者が対象(調整控除配偶者 h)
        => h.氏名あり && h.特別障害 && 配偶者収入要件(h);

    private static bool 配偶者収入要件(調整控除配偶者 h) => h.収入種類 switch
    {
        収入種類.給与 or 収入種類.年金 => h.金額 <= 配偶者_給与年金上限,
        収入種類.所得 => h.金額 <= 配偶者_所得上限,
        _ => false,
    };

    private bool 扶養が対象(調整控除扶養親族 f, int 判定年)
        => (AgeClassifier.は23歳未満(f.生年月日, 判定年) || f.特別障害)
           && f.特定親族特別控除額 == 0m
           && 扶養収入要件(f, 判定年);

    private bool 扶養収入要件(調整控除扶養親族 f, int 判定年) => f.収入種類 switch
    {
        収入種類.給与 => f.金額 <= 扶養_給与上限,
        収入種類.年金 => 年金所得(f) <= 扶養_所得上限,
        収入種類.所得 => f.金額 <= 扶養_所得上限,
        収入種類.その他 => true, // 現行 CMB_SZK_NKN_1（収入種類が空欄）は扶養に該当
        _ => false,
    };

    private decimal 年金所得(調整控除扶養親族 f)
    {
        if (_年金所得換算 is null)
            throw new InvalidOperationException(
                "年金収入の扶養親族を判定するには 年金所得換算（現行 calNenkinSyotoku 相当）の注入が必要です。");
        return _年金所得換算(f.金額, f.生年月日);
    }
}
