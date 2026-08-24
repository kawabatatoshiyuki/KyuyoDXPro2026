using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 源泉所得税（甲欄・月額）の計算。国税庁「給与所得の源泉徴収税額の電算機計算の特例」に準拠。
/// 別表は <see cref="WithholdingTaxTables"/> として外部注入（現行DBの別表マスタ相当）。
///
/// 現行（F_給与処理）の実装に一致させてある：
///  ・給与所得控除（別表1）= Ceiling(A × 控除率) + 控除額（率部は小数点以下切上げ）
///  ・課税給与所得 WK = A − 給与所得控除 − 扶養控除 − 基礎控除（0以下なら税0、1000円丸めなし）
///  ・税額 = (WK × 税率 − 控除額) を 10円未満四捨五入
/// （会社別の端数設定 hasuCode は残業手当計算用であり、所得税には適用しない）
/// </summary>
public sealed class WithholdingTaxCalculator
{
    private readonly WithholdingTaxTables _t;

    public WithholdingTaxCalculator(WithholdingTaxTables tables) => _t = tables;

    /// <summary>
    /// その月の源泉徴収税額を計算する。
    /// </summary>
    /// <param name="社会保険料控除後金額">課税支給 − 社会保険料等合計（その月の課税対象額）。</param>
    /// <param name="扶養親族等の数">控除対象配偶者を含む人数。</param>
    public decimal Calculate(decimal 社会保険料控除後金額, int 扶養親族等の数)
    {
        decimal a = 社会保険料控除後金額;
        if (a <= 0) return 0m;

        decimal 給与所得控除 = Lookup(_t.給与所得控除, a);
        decimal 基礎控除 = Lookup(_t.基礎控除, a);
        decimal 扶養控除 = _t.扶養控除単価 * Math.Max(0, 扶養親族等の数);

        // 課税給与所得金額（マイナスは0）
        decimal b = a - 給与所得控除 - 基礎控除 - 扶養控除;
        if (b <= 0) return 0m;

        TaxRateRow row = _t.税率速算.First(r => b >= r.下限 && b < r.上限);
        decimal tax = b * row.税率 - row.控除額;
        if (tax < 0) tax = 0m;

        return RoundTax(tax);
    }

    /// <summary>別表（区分内 率＋加算額）から控除額を求める。率部は小数点以下切上げ（現行準拠）。</summary>
    private static decimal Lookup(IReadOnlyList<BracketRow> table, decimal a)
    {
        BracketRow row = table.First(r => a >= r.下限 && a < r.上限);
        return Math.Ceiling(a * row.率) + row.加算額;
    }

    /// <summary>最終税額の端数処理：10円未満四捨五入（現行 F_給与処理 に一致）。</summary>
    private static decimal RoundTax(decimal tax) => Math.Round(tax / 10m, MidpointRounding.AwayFromZero) * 10m;
}
