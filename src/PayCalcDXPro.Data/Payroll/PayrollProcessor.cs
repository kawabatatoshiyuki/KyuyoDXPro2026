using PayCalcDXPro.Data.Rate;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;

namespace PayCalcDXPro.Data.Payroll;

/// <summary>
/// 月次給与の計算ブリッジ。現行 WinForms（F_給与処理）の計算を 1:1 で再現し、
/// 入力済みの <see cref="PayrollEntry"/> に結果（各保険・所得税・各合計・データ個数・非課税金額計）を書き込む。
///
/// 構成：
///  ・社会保険＝現行「控除項目計算」ボタンの忠実再現（保険料額表の小数値＋料率／50銭処理）。
///  ・所得税＝検証済みドメイン計算器（甲＝<see cref="WithholdingTaxCalculator"/>／乙＝<see cref="OtsuWithholdingTaxCalculator"/>）。
///  ・総支給〜差引支給・データ個数・非課税金額計＝現行 kyuyoCalc の忠実再現。
///
/// 基本給（日給者）と時間外手当は現行の各ボタンに対応する <see cref="ComputeDailyBasePay"/>／
/// <see cref="ComputeOvertimeAllowance"/> で算出する（<see cref="Apply"/> は入力済みの値を使う）。
/// </summary>
public sealed class PayrollProcessor
{
    private readonly IReadOnlyList<RateTableRow> _額表;
    private readonly RateSettings _料率;
    private readonly WithholdingTaxCalculator _甲;
    private readonly OtsuWithholdingTaxCalculator? _乙;

    public PayrollProcessor(RateBundle rates, WithholdingTaxTables 甲表, OtsuWithholdingTaxCalculator? 乙 = null)
    {
        _額表 = rates.額表;
        _料率 = rates.料率;
        _甲 = new WithholdingTaxCalculator(甲表);
        _乙 = 乙;
    }

    // ============================================================
    // 結果の確定（kyuyoCalc 相当）
    // ============================================================

    /// <summary>
    /// 入力済みエントリ（基本給・各手当・時間外手当・勤怠控除・通勤費・住民税・控除項目・還付/徴収）から
    /// 社会保険・所得税・各合計を確定し、<paramref name="e"/> に書き込む。
    /// </summary>
    public void Apply(PayrollEntry e, PayrollEmployeeSettings emp)
    {
        // ---- 総支給額（雇用保険の算定にも使う）----
        int 総支給 = (e.基本給 ?? 0);
        for (int i = 0; i < 12; i++) 総支給 += e.給与支給手当[i] ?? 0;
        総支給 += e.時間外手当 ?? 0;
        総支給 -= e.勤怠控除 ?? 0;
        総支給 += e.課税通勤費 ?? 0;
        総支給 += e.非課税通勤費 ?? 0;
        e.総支給額 = 総支給;

        // ---- 社会保険（加入項目のみ算出。控除項目計算ボタン相当）----
        ApplySocialInsurance(e, emp, 総支給);

        // 所得税の算定には課税対象額が必要なので、先に合計類（kyuyoCalc）を確定する。
        RecomputeTotals(e, emp.非課税手当);

        // ---- 所得税（甲＝月額表／乙＝乙税表／計算しない＝据え置き）----
        switch (emp.源泉区分)
        {
            case 源泉区分.甲:
                e.所得税 = (int)_甲.Calculate(e.課税対象額 ?? 0, emp.源泉扶養親族数);
                break;
            case 源泉区分.乙:
                e.所得税 = (int)(_乙 ?? throw new InvalidOperationException(
                    "乙欄計算には OtsuWithholdingTaxCalculator が必要です。"))
                    .Calculate(e.課税対象額 ?? 0, emp.源泉扶養親族数);
                break;
            // 計算しない：所得税は変更しない。
        }

        // 所得税確定後に総控除・差引を再確定する。
        RecomputeTotals(e, emp.非課税手当);

        e.扶養親族数 = emp.源泉扶養親族数;
    }

    /// <summary>
    /// 個々の支給・控除額（保険料・所得税は現在の入力値）から合計類のみを再計算する（現行 kyuyoCalc 相当）。
    /// 保険料・所得税の自動算出は行わない＝各金額の手入力変更時に
    /// 総支給額／社会保険合計／課税対象額／総控除額／差引支給額を即時更新するためのもの。
    /// </summary>
    /// <param name="非課税手当">手当1〜12が非課税かどうか（長さ12想定）。課税対象額の算定に用いる。</param>
    public static void RecomputeTotals(PayrollEntry e, bool[] 非課税手当)
    {
        // 総支給額。
        int 総支給 = (e.基本給 ?? 0);
        for (int i = 0; i < 12; i++) 総支給 += e.給与支給手当[i] ?? 0;
        総支給 += e.時間外手当 ?? 0;
        総支給 -= e.勤怠控除 ?? 0;
        総支給 += e.課税通勤費 ?? 0;
        総支給 += e.非課税通勤費 ?? 0;
        e.総支給額 = 総支給;

        // 社会保険合計＝健保＋介護＋子育支援＋厚年＋年金基金＋雇用＋小規模共済（#161対応）。
        int 社保合計 = (e.健康保険 ?? 0) + (e.介護保険 ?? 0) + (e.子育支援 ?? 0) + (e.厚生年金 ?? 0)
                     + (e.年金基金 ?? 0) + (e.雇用保険 ?? 0) + (e.小規模共済 ?? 0);
        e.社会保険合計 = 社保合計;

        // 課税対象額＝max(0, 総支給 −(非課税通勤費＋非課税手当合計)− 社会保険合計)。
        int 非課税手当合計 = 0;
        for (int i = 0; i < 12; i++)
            if (i < 非課税手当.Length && 非課税手当[i]) 非課税手当合計 += e.給与支給手当[i] ?? 0;
        int 課税対象 = 総支給 - (e.非課税通勤費 ?? 0) - 非課税手当合計 - 社保合計;
        e.課税対象額 = 課税対象 > 0 ? 課税対象 : 0;

        // 総控除額＝社会保険合計＋所得税＋住民税＋控除項目1〜8。
        int 総控除 = 社保合計 + (e.所得税 ?? 0) + (e.住民税 ?? 0);
        for (int i = 0; i < 8; i++) 総控除 += e.控除項目[i] ?? 0;
        e.総控除額 = 総控除;

        // 差引支給額＝総支給 − 総控除 ＋ 還付金額 − 徴収金額。
        e.差引支給額 = 総支給 - 総控除 + (e.還付金額 ?? 0) - (e.徴収金額 ?? 0);

        // 保存用の付帯項目。
        e.非課税金額計 = 非課税手当合計;
        e.データ個数 = データ個数(e);
    }

    // ============================================================
    // 社会保険（控除項目計算ボタン相当）
    // ============================================================

    private void ApplySocialInsurance(PayrollEntry e, PayrollEmployeeSettings emp, int 総支給)
    {
        bool 金額ゼロ = emp.報酬月額 <= 0;
        RateTableRow? b = 金額ゼロ ? null : Bracket(emp.報酬月額);
        bool 表なし = 金額ゼロ || b is null;

        // 健康保険：額表健保に50銭処理。
        if (emp.健康保険加入)
            e.健康保険 = 表なし ? 0 : Round50((decimal)(b!.健康保険 ?? 0));

        // 介護保険：50銭処理(額表健保＋額表介護) − 健康保険。
        if (emp.介護保険加入)
        {
            if (表なし) e.介護保険 = 0;
            else
            {
                decimal kenko = (decimal)(b!.健康保険 ?? 0);
                decimal kaigo = (decimal)(b.介護保険 ?? 0);
                e.介護保険 = Round50(kenko + kaigo) - (e.健康保険 ?? 0);
            }
        }

        // 子育支援：額表 子育支援（整数の直読み。50銭処理なし。#161対応）。
        if (emp.子育支援加入)
            e.子育支援 = 表なし ? 0 : (int)(b!.子育支援 ?? 0);

        // 厚生年金：額表厚生年金（整数）。
        if (emp.厚生年金加入)
            e.厚生年金 = 表なし ? 0 : (int)(b!.厚生年金 ?? 0);

        // 年金基金：額表厚生年金基金（整数）。
        if (emp.年金基金加入)
            e.年金基金 = 表なし ? 0 : (int)(b!.厚生年金基金 ?? 0);

        // 雇用保険：総支給 × 料率 / 1000 を50銭処理（労働保険適用区分で一般／一般以外）。
        if (emp.雇用保険加入)
        {
            double rate = emp.労働保険適用 == "2"
                ? (_料率.雇用保険一般以外 ?? 0)
                : (_料率.雇用保険一般 ?? 0);
            decimal raw = (decimal)総支給 * (decimal)rate / 1000m;
            e.雇用保険 = Round50Signed(raw);
        }
    }

    /// <summary>報酬月額から保険料額表の行を引く（報酬月額_自 ≤ 報酬 ＜ 報酬月額_至）。</summary>
    private RateTableRow? Bracket(int 報酬)
        => _額表.FirstOrDefault(r =>
            (r.報酬月額自 ?? 0) <= 報酬 &&
            (long)(r.報酬月額至 ?? int.MaxValue) > 報酬);

    // ============================================================
    // 基本給・時間外手当（各計算ボタン相当）
    // ============================================================

    /// <summary>日給者の基本給＝端数処理(出勤日数 × 日給単価)。</summary>
    public int ComputeDailyBasePay(double 出勤日数, PayrollEmployeeSettings emp)
        => 端数処理((decimal)出勤日数 * emp.日給単価, emp.端数処理);

    /// <summary>
    /// 時間外手当＝Σ(残業単価 × 残業時間)。残業時間は H.MM 表記なので時間へ換算してから乗ずる。
    /// 合計に会社の端数処理（四捨五入／切捨／切上）を適用する。
    /// </summary>
    public int ComputeOvertimeAllowance(PayrollEntry e, PayrollEmployeeSettings emp)
    {
        decimal sum =
            emp.法定内残業単価 * 時分換算(e.法定内残業) +
            emp.普通残業単価 * 時分換算(e.普通残業60h以下) +
            emp.普通残業単価60h超 * 時分換算(e.普通残業60h超) +
            emp.深夜残業単価 * 時分換算(e.深夜残業60h以下) +
            emp.深夜残業単価60h超 * 時分換算(e.深夜残業60h超) +
            emp.休日残業単価 * 時分換算(e.休日出勤60h以下) +
            emp.休日残業単価60h超 * 時分換算(e.休日出勤60h超) +
            emp.休日深夜残業単価 * 時分換算(e.休日深夜60h以下) +
            emp.休日深夜残業単価60h超 * 時分換算(e.休日深夜60h超);
        return 端数処理(sum, emp.端数処理);
    }

    /// <summary>
    /// 時間外手当を区分別（略称＋金額）に算出する。各区分＝端数処理(残業単価 × 残業時間)。
    /// 合計を丸める <see cref="ComputeOvertimeAllowance"/> と異なり、区分ごとに端数処理するため
    /// 各金額の合算がそのまま時間外手当になる（＝内訳の合計と時間外手当が必ず一致する）。備考への内訳表示に使う。
    /// </summary>
    public IReadOnlyList<(string 略称, int 金額)> ComputeOvertimeAllowanceParts(PayrollEntry e, PayrollEmployeeSettings emp)
    {
        int R(int 単価, double? 時間) => 端数処理(単価 * 時分換算(時間), emp.端数処理);
        return new (string, int)[]
        {
            ("法定内",   R(emp.法定内残業単価,        e.法定内残業)),
            ("普通",     R(emp.普通残業単価,          e.普通残業60h以下)),
            ("普通60超", R(emp.普通残業単価60h超,     e.普通残業60h超)),
            ("深夜",     R(emp.深夜残業単価,          e.深夜残業60h以下)),
            ("深夜60超", R(emp.深夜残業単価60h超,     e.深夜残業60h超)),
            ("休日",     R(emp.休日残業単価,          e.休日出勤60h以下)),
            ("休日60超", R(emp.休日残業単価60h超,     e.休日出勤60h超)),
            ("休深",     R(emp.休日深夜残業単価,      e.休日深夜60h以下)),
            ("休深60超", R(emp.休日深夜残業単価60h超, e.休日深夜60h超)),
        };
    }

    // ============================================================
    // 端数処理ヘルパー
    // ============================================================

    /// <summary>H.MM 表記（例 1.30 ＝ 1時間30分）を時間（1.5）へ換算（現行 chgMinuteHour）。</summary>
    private static decimal 時分換算(double? value)
    {
        decimal minute = (decimal)(value ?? 0);
        decimal 小数 = minute % 1m;
        return (minute - 小数) + (小数 / 0.6m);
    }

    /// <summary>残業手当・日給の端数処理（1=四捨五入／2=切捨／3=切上、現行 hasuSyori）。</summary>
    private static int 端数処理(decimal value, int code) => code switch
    {
        1 => (int)decimal.Round(value, MidpointRounding.AwayFromZero),
        2 => (int)decimal.Floor(value),
        3 => (int)decimal.Ceiling(value),
        _ => 0,
    };

    /// <summary>50銭処理（小数部 ＞ 0.5 で切上、それ以外は切捨）。健保・介護に使用。</summary>
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

    /// <summary>データ個数＝int変換でき、かつ 0 より大きい項目の数（現行 dataCount）。</summary>
    private static int データ個数(PayrollEntry e)
    {
        int n = 0;
        void C(int? v) { if ((v ?? 0) > 0) n++; }
        C(e.基本給);
        for (int i = 0; i < 12; i++) C(e.給与支給手当[i]);
        C(e.時間外手当); C(e.勤怠控除); C(e.課税通勤費); C(e.非課税通勤費);
        C(e.総支給額); C(e.健康保険); C(e.介護保険); C(e.子育支援); C(e.厚生年金); C(e.年金基金); C(e.小規模共済); C(e.雇用保険);
        C(e.社会保険合計); C(e.課税対象額); C(e.所得税); C(e.住民税);
        for (int i = 0; i < 8; i++) C(e.控除項目[i]);
        C(e.総控除額); C(e.差引支給額);
        return n;
    }
}
