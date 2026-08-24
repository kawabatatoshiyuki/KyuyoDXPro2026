using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 月次給与の計算オーケストレーション。
/// 現行 WinForms（F_給与処理）の画面コードビハインドに散在していた計算を、
/// UI・DB に依存しない純粋ロジックとして1か所へ集約したもの（移行のドメイン中核）。
/// 料率・別表を注入するだけで、デスクトップ版／ブラウザ版のどちらからも同一ロジックを呼べる。
/// </summary>
public sealed class PayrollCalculator
{
    private readonly ISocialInsuranceCalculator _si;
    private readonly WithholdingTaxCalculator _wt;
    private readonly OtsuWithholdingTaxCalculator? _otsu;

    /// <summary>社会保険の算出方式（表引き／料率）と甲・乙の所得税計算器を注入する本番向けコンストラクタ。</summary>
    public PayrollCalculator(
        ISocialInsuranceCalculator socialInsurance,
        WithholdingTaxTables taxTables,
        OtsuWithholdingTaxCalculator? otsuTaxCalculator = null)
    {
        _si = socialInsurance;
        _wt = new WithholdingTaxCalculator(taxTables);
        _otsu = otsuTaxCalculator;
    }

    /// <summary>料率からの計算で組み立てる簡易コンストラクタ（検証・概算用）。</summary>
    public PayrollCalculator(InsuranceRateSet rates, WithholdingTaxTables taxTables)
        : this(new SocialInsuranceCalculator(rates), taxTables)
    {
    }

    public PayrollResult Calculate(PayrollInput input)
    {
        decimal 総支給 = input.課税支給合計 + input.非課税支給合計;

        SocialInsuranceResult si = _si.Calculate(input.標準報酬月額, 総支給, input.介護保険対象);

        // 源泉所得税の課税対象は「課税支給 − 社会保険料等合計（雇用保険含む）」
        decimal 課税対象 = input.課税支給合計 - si.合計;
        decimal 所得税 = input.源泉区分 == 源泉区分.乙
            ? (_otsu ?? throw new InvalidOperationException("乙欄計算には OtsuWithholdingTaxCalculator が必要です。"))
                .Calculate(課税対象, input.扶養親族等の数)
            : _wt.Calculate(課税対象, input.扶養親族等の数);

        decimal 控除合計 = si.合計 + 所得税 + input.その他控除合計;
        decimal 差引支給額 = 総支給 - 控除合計;

        return new PayrollResult
        {
            総支給額 = 総支給,
            社会保険 = si,
            所得税 = 所得税,
            その他控除合計 = input.その他控除合計,
            控除合計 = 控除合計,
            差引支給額 = 差引支給額,
        };
    }
}
