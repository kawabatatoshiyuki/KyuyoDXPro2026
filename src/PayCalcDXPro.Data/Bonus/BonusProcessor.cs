using PayCalcDXPro.Data.Payroll;
using PayCalcDXPro.Data.Rate;
using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Data.Bonus;

/// <summary>
/// 賞与の計算ブリッジ。現行 WinForms（F_賞与処理）の計算を 1:1 で再現し、
/// 入力済みの <see cref="BonusEntry"/> に結果（各保険・所得税・各合計・データ個数）を書き込む。
///
/// 月次給与（<see cref="PayrollProcessor"/>）との違い：
///  ・社会保険は保険料額表ではなく <b>標準賞与額 × 被保険者負担率(料率) ＋ 50銭処理</b>で算出する。
///    健保・介護・子育支援は健保基準額 syahoKaigoWk（年度累計上限573万・複数回通算）、
///    厚年・年金基金は年金基準額 nenkinWk（1回上限150万）を用いる。
///  ・所得税は前月給与の課税対象額から賞与算出率表（甲／乙）で率を引く特別方式。
///  ・非課税・通勤費・住民税は無く、課税対象＝総支給−社会保険合計。総控除は控除項目9まで含める。
///
/// #161対応：子育支援（子ども・子育て支援金 被保険者負担分）を社会保険合計に算入する。
/// </summary>
public sealed class BonusProcessor
{
    // 現行 MConst の上限額。
    private const decimal 健保介護_年度上限 = 5_730_000m; // SYOYO_SYAKAIHOKEN_JOUGENGAKU
    private const decimal 厚年_1回上限 = 1_500_000m;       // SYOYO_NENKIN_JOUGENGAKU

    private readonly RateSettings _料率;
    private readonly BonusTaxRateTable _甲;
    private readonly BonusOtsuRateTable? _乙;

    public BonusProcessor(RateSettings 料率, BonusTaxRateTable 甲表, BonusOtsuRateTable? 乙表 = null)
    {
        _料率 = 料率;
        _甲 = 甲表;
        _乙 = 乙表;
    }

    /// <summary>
    /// 入力済みエントリ（賞与・各支給項目・控除項目・還付/徴収）から
    /// 社会保険・所得税・各合計を確定し、<paramref name="e"/> に書き込む。
    /// </summary>
    public void Apply(BonusEntry e, BonusEmployeeSettings emp, BonusContext ctx)
    {
        // ---- 総支給額（社会保険の算定にも使う）----
        int 総支給 = e.賞与 ?? 0;
        for (int i = 0; i < 16; i++) 総支給 += e.賞与支給[i] ?? 0;
        e.総支給額 = 総支給;

        // ---- 社会保険（加入項目のみ算出。控除項目計算ボタン相当）----
        ApplySocialInsurance(e, emp, ctx, 総支給);

        // 賞与所得税の算定には課税対象額が必要なので、先に合計類を確定する。
        RecomputeTotals(e);

        // ---- 賞与所得税（前月給与の課税対象額から算出率表で率を引く）----
        e.所得税 = ComputeBonusTax(emp, ctx, e.課税対象額 ?? 0);

        // 所得税確定後に総控除・差引を再確定する。
        RecomputeTotals(e);

        // 扶養親族数＝源泉徴収上の扶養親族等の数（現行は従業員情報の値をそのまま保存）。
        e.扶養親族数 = emp.源泉扶養親族数;
    }

    /// <summary>
    /// 個々の支給・控除額（保険料・所得税は現在の入力値）から合計類のみを再計算する（現行 kyuyoCalc 相当）。
    /// 保険料・賞与所得税の自動算出は行わない＝各金額の手入力変更時に
    /// 総支給額／社会保険合計／課税対象額／総控除額／差引支給額を即時更新するためのもの。
    /// </summary>
    public static void RecomputeTotals(BonusEntry e)
    {
        // 総支給額。
        int 総支給 = e.賞与 ?? 0;
        for (int i = 0; i < 16; i++) 総支給 += e.賞与支給[i] ?? 0;
        e.総支給額 = 総支給;

        // 社会保険合計＝健保＋介護＋子育支援＋厚年＋年金基金＋雇用＋小規模共済。
        int 社保合計 = (e.健康保険 ?? 0) + (e.介護保険 ?? 0) + (e.子育支援 ?? 0) + (e.厚生年金 ?? 0)
                     + (e.年金基金 ?? 0) + (e.雇用保険 ?? 0) + (e.小規模共済 ?? 0);
        e.社会保険合計 = 社保合計;

        // 課税対象額＝max(0, 総支給 − 社会保険合計)（賞与は非課税・通勤費なし）。
        int 課税対象 = 総支給 - 社保合計;
        e.課税対象額 = 課税対象 > 0 ? 課税対象 : 0;

        // 総控除額＝社会保険合計＋所得税＋控除項目1〜9（賞与は控除9も含む）。
        int 総控除 = 社保合計 + (e.所得税 ?? 0);
        for (int i = 0; i < 9; i++) 総控除 += e.控除項目[i] ?? 0;
        e.総控除額 = 総控除;

        // 差引支給額＝総支給 − 総控除 ＋ 還付金額 − 徴収金額。
        e.差引支給額 = 総支給 - 総控除 + (e.還付金額 ?? 0) - (e.徴収金額 ?? 0);

        e.データ個数 = データ個数(e);
    }

    // ============================================================
    // 社会保険（控除項目計算ボタン相当・全て料率方式＋50銭）
    // ============================================================

    private void ApplySocialInsurance(BonusEntry e, BonusEmployeeSettings emp, BonusContext ctx, int 総支給)
    {
        decimal 総 = 総支給;

        // 健保・介護・子育支援の標準賞与額 syahoKaigoWk（年度累計573万が上限。複数回は通算）。
        decimal syaho = Floor千円(総);
        foreach (var 既 in ctx.既支給賞与総支給) syaho += Floor千円(既);
        if (syaho > 健保介護_年度上限)
        {
            syaho = Floor千円(総 - (syaho - 健保介護_年度上限));
            // 2回目以降で上限超過によりマイナスになった場合は0（現行準拠）。
            if (ctx.既支給賞与総支給.Count > 0 && syaho < 0) syaho = 0m;
        }
        else
        {
            syaho = Floor千円(総);
        }

        // 厚年・年金基金の標準賞与額 nenkinWk（1回150万が上限。通算しない）。
        decimal nenkin = 総 > 厚年_1回上限 ? 厚年_1回上限 : Floor千円(総);

        // 健康保険＝round50(標準賞与額 × 健保率/100)。
        int 健保 = 0;
        if (emp.健康保険加入)
        {
            健保 = Round50(syaho * Rate(_料率.健康保険本人) / 100m);
            e.健康保険 = 健保;
        }

        // 介護保険＝round50(round50(健保分)＋round50(介護分)) − 健康保険（現行の組合せ50銭を忠実再現）。
        // ※健保未加入のときは健康保険控除分が0となり、健保＋介護の合算が介護保険に乗る点まで再現する。
        if (emp.介護保険加入)
        {
            int kenkoK = Round50(syaho * Rate(_料率.健康保険本人) / 100m);
            int kaigoK = Round50(syaho * Rate(_料率.介護保険本人) / 100m);
            int txtKenko = emp.健康保険加入 ? 健保 : 0;
            e.介護保険 = Round50((decimal)(kenkoK + kaigoK)) - txtKenko;
        }

        // 厚生年金＝round50(年金基準額 × 厚年率/100)。
        if (emp.厚生年金加入)
            e.厚生年金 = Round50(nenkin * Rate(_料率.厚生年金本人) / 100m);

        // 年金基金＝round50(年金基準額 × 厚生年金基金率/100)。
        if (emp.年金基金加入)
            e.年金基金 = Round50(nenkin * Rate(_料率.厚生年金基金本人) / 100m);

        // 子育支援＝round50(健保基準額 × 子育支援率/100)（#161対応）。
        if (emp.子育支援加入)
            e.子育支援 = Round50(syaho * Rate(_料率.子育支援本人) / 100m);

        // 雇用保険＝総支給 × 雇用保険料率 / 1000 を50銭処理（労働保険適用区分で一般／一般以外）。
        if (emp.雇用保険加入)
        {
            double rate = emp.労働保険適用 == "2"
                ? (_料率.雇用保険一般以外 ?? 0)
                : (_料率.雇用保険一般 ?? 0);
            e.雇用保険 = Round50Signed(総 * (decimal)rate / 1000m);
        }
    }

    // ============================================================
    // 賞与所得税（税額計算ボタン相当）
    // ============================================================

    private int ComputeBonusTax(BonusEmployeeSettings emp, BonusContext ctx, int 賞与課税対象)
    {
        // 前月給与トランが無ければ0（現行：給与トラン未取得時は0）。
        if (ctx.前月給与課税対象額 is null) return 0;

        decimal 前月 = ctx.前月給与課税対象額.Value;
        decimal 課税 = 賞与課税対象;

        // 賞与の課税対象が前月給与（課税対象）の10倍を超える場合は0（現行準拠）。
        if (前月 * 10m < 課税) return 0;

        decimal 率 = emp.源泉区分 switch
        {
            Payroll.源泉区分.甲 => _甲.LookupRatePercent(前月, emp.源泉扶養親族数),
            Payroll.源泉区分.乙 => (_乙 ?? throw new InvalidOperationException(
                "乙欄の賞与計算には BonusOtsuRateTable が必要です。")).LookupRatePercent(前月),
            _ => 0m, // 計算しない（"3"）は0。
        };

        return (int)decimal.Floor(課税 * 率 / 100m);
    }

    // ============================================================
    // ヘルパー
    // ============================================================

    private static decimal Rate(double? v) => (decimal)(v ?? 0);

    private static decimal Floor千円(decimal v) => decimal.Floor(v / 1000m) * 1000m;

    /// <summary>50銭処理（小数部 ＞ 0.5 で切上、それ以外は切捨）。</summary>
    private static int Round50(decimal v)
        => (v % 1m) > 0.5m ? (int)(decimal.Floor(v) + 1) : (int)decimal.Floor(v);

    /// <summary>雇用保険用の50銭処理（現行の負数分岐込み）。</summary>
    private static int Round50Signed(decimal raw)
    {
        decimal frac = raw % 1m;
        if (frac > 0.5m) return (int)(decimal.Floor(raw) + 1);
        if (frac < -0.5m) return (int)decimal.Floor(raw);
        if (frac < 0m && frac >= -0.4m) return (int)decimal.Round(raw);
        return (int)decimal.Floor(raw);
    }

    /// <summary>データ個数＝int変換でき、かつ 0 より大きい項目の数（現行 dataCount。控除9・還付/徴収は数えない）。</summary>
    private static int データ個数(BonusEntry e)
    {
        int n = 0;
        void C(int? v) { if ((v ?? 0) > 0) n++; }
        C(e.賞与);
        for (int i = 0; i < 16; i++) C(e.賞与支給[i]);
        C(e.総支給額);
        C(e.健康保険); C(e.介護保険); C(e.子育支援); C(e.厚生年金); C(e.年金基金); C(e.小規模共済); C(e.雇用保険);
        C(e.社会保険合計); C(e.課税対象額); C(e.所得税);
        for (int i = 0; i < 8; i++) C(e.控除項目[i]); // 控除1〜8（控除9は数えない）
        C(e.総控除額); C(e.差引支給額);
        return n;
    }
}
