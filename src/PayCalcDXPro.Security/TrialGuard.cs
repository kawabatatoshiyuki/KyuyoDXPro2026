namespace PayCalcDXPro.Security;

public enum TrialBlockReason
{
    None,           // 起動可
    DifferentYear,  // 別年度の体験版の形跡（年度ロック）
    Tampered,       // マーカー改ざん/破損
}

/// <summary>体験版の年度ロック判定結果。</summary>
public sealed class TrialEvaluation
{
    public bool Blocked { get; init; }
    public TrialBlockReason Reason { get; init; }
    public string Message { get; init; } = "";
    public int FirstTrialYear { get; init; }
}

/// <summary>
/// 体験版の年度ロック判定（現行デスクトップ版 <c>taikenbanCheck</c> の移植）。
/// 利用期限による制限はなし（無期限）。初回起動で現在年度のマーカーを作成し、
/// 以後は同一年度（パッチ更新版を含む）のみ起動可。別年度の形跡・改ざんはブロックする。
/// シングルトンで一度だけ評価し、結果をキャッシュする。
/// </summary>
public sealed class TrialGuard
{
    private readonly bool _enabled;
    private readonly int _currentYear;
    private readonly Lazy<TrialEvaluation> _eval;

    /// <param name="enabled">体験版チェックの有効/無効（製品版ビルドでは false）。</param>
    /// <param name="currentYear">セットアップ年度（現行 SYR_NEN 相当）。</param>
    public TrialGuard(bool enabled, int currentYear)
    {
        _enabled = enabled;
        _currentYear = currentYear;
        _eval = new Lazy<TrialEvaluation>(Evaluate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>年度ロック判定（初回はここでマーカーを作成）。結果はキャッシュされる。</summary>
    public TrialEvaluation Evaluation => _eval.Value;

    private TrialEvaluation Evaluate()
    {
        // 無効化（製品版）・年度不正は制限しない（安全側で起動許可）。
        if (!_enabled || _currentYear <= 0) return new TrialEvaluation();

        TrialMarker.MarkerInfo info = TrialMarker.Read();
        switch (info.Status)
        {
            case TrialMarker.MarkerStatus.NotFound:
                // 初回起動：現在年度でマーカーを作成し、無制限で起動する。
                try { TrialMarker.Write(_currentYear); }
                catch { /* 書込不可でも起動は許可（安全側） */ }
                return new TrialEvaluation();

            case TrialMarker.MarkerStatus.Valid:
                // 同一年度（パッチ更新版を含む）は無制限で起動する。
                if (info.FirstTrialYear == _currentYear) return new TrialEvaluation();
                // 別年度の体験版の形跡あり → 起動ブロック（現行 MN035I）。
                return new TrialEvaluation
                {
                    Blocked = true,
                    Reason = TrialBlockReason.DifferentYear,
                    FirstTrialYear = info.FirstTrialYear,
                    Message = $"既に別バージョン（{info.FirstTrialYear}年版）の体験版をご利用の形跡があります。\n"
                            + "体験版は最初にセットアップされた版のみご利用いただけます。",
                };

            default:
                // 署名検証NG（改ざん/破損）→ 安全側でブロック（現行 MN036I）。
                return new TrialEvaluation
                {
                    Blocked = true,
                    Reason = TrialBlockReason.Tampered,
                    Message = "体験版の利用情報を確認できませんでした。\n体験版を正しくセットアップし直してください。",
                };
        }
    }
}
