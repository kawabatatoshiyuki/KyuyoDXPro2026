using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;

namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 年末調整の年税額計算（入力＋年間集計 → 結果）を <see cref="YearEndEntry"/> に対して適用する。
/// 税額表を引数で受け取り Web 非依存にしてあるため、現行突合テストから直接呼べる。
/// 計算本体は現行 F_年調_従業員情報 に一致する <see cref="YearEndAdjustmentCalculator"/> に委譲する。
/// </summary>
public static class YearEndCalculation
{
    /// <summary>
    /// 「計」（給与計・社保計・源泉計・給与賞与計）を現在の入力から再集計して entry に設定する。
    /// 年間集計（給与[]/賞与[]/社保[]/源泉[]）はトラン由来でロード済みの値を用い、
    /// 本人情報タブの前社分・非課税通勤手当の手入力をここで反映する。
    /// 現行 F_年調_従業員情報 nenmatuWorkEdit（給与計＝前社収入＋Σ給与−非課税通勤手当金額 ほか）に一致。
    /// </summary>
    public static void RecomputeTotals(YearEndEntry e)
    {
        // 給与計＝前社収入＋Σ給与−非課税通勤手当金額（非課税通勤手当は支給給与から控除する調整額）。
        e.給与計 = e.前社収入 + e.給与.Sum() - e.非課税通勤手当金額;
        e.給与社保計 = e.前社社保 + e.社保.Sum();
        e.給与源泉計 = e.前社源泉 + e.源泉.Sum();
        e.賞与計 = e.賞与.Sum();
        e.賞与社保計 = e.社保賞与.Sum();
        e.賞与源泉計 = e.源泉賞与.Sum();
        e.給与賞与計 = e.給与計 + e.賞与計;
        e.源泉計 = e.給与源泉計 + e.賞与源泉計;
    }

    /// <summary>
    /// 年末調整を行わない（乙欄＝未済「2」/乙欄「3」）場合に、年調の結果欄・自動算定値をすべてクリアする。
    /// 現行 F_年調_従業員情報 nenmatuWorkEdit の else 節（給与所得控除後〜年調年税額・過不足額を null）に一致。
    /// 「計」（給与計・源泉計 等）は年調有無にかかわらず再集計するためここでは触れない。
    /// </summary>
    public static void ClearYearEndResults(YearEndEntry e)
    {
        e.給与所得控除後 = 0;
        e.給与所得控除後調整控除後 = 0;
        e.所得金額調整控除額 = 0;
        e.生保控除 = 0;
        e.地震控除 = 0;
        e.配偶者特別控除 = 0;
        e.基礎控除 = 0;
        e.所得控除計 = 0;
        e.課税所得 = 0;
        e.所得税 = 0;
        e.年調所得税額 = 0;
        e.年調年税額 = 0;
        e.過不足額 = 0;
        // 配偶者・扶養親族からの自動算定値も年調しないときはクリア（現行 else：配偶者所得/配偶者控除/扶養控除等合計 を null）。
        e.配偶者あり = false;
        e.配偶者所得 = 0;
        e.配偶者70歳以上 = false;
        e.扶養控除等合計 = 0;
        e.特定親族特別控除額 = 0;
    }

    /// <summary>入力値・年間集計から年税額を計算し、結果列を entry に反映する（保存はしない）。</summary>
    public static void Apply(YearEndEntry e, IncomeTaxYearTables yearTables, IncomeDeductionTables dedTables)
    {
        int 地震控除 = 地震保険料控除(e.地震保険, e.長期損害);

        var input = new YearEndDeductionInput
        {
            年間給与収入 = e.給与賞与計,
            // 実効適用（自動判定済み）を優先。単体 Apply 呼び出し時は手動フラグにフォールバック。
            所得金額調整控除の適用 = e.所得金額調整控除実効適用 || e.所得金額調整控除適用,
            社会保険料控除 = e.給与社保計 + e.賞与社保計,
            // 現行 nenmatuWorkEdit の所得控除計は申告社保のみ（国民年金保険料等は申告書転記・源泉徴収票摘要欄の表示専用で非加算）。
            申告社会保険料 = e.申告社保,
            小規模企業共済等掛金 = e.小規模共済,
            介護医療保険料 = e.介護医療保険,
            一般生命保険料_新 = e.新生命保険,
            一般生命保険料_旧 = e.生命保険,
            個人年金保険料_新 = e.新個人年金,
            個人年金保険料_旧 = e.個人年金,
            地震保険料控除 = 地震控除,
            配偶者あり = e.配偶者あり,
            配偶者合計所得 = e.配偶者所得,
            配偶者70歳以上 = e.配偶者70歳以上,
            扶養控除等合計 = e.扶養控除等合計,
            特定親族特別控除合計 = e.特定親族特別控除額,
            住宅借入金等特別控除 = e.住宅控除,
        };

        var r = new YearEndAdjustmentCalculator(yearTables)
            .CalculateWithDeductions(input, new IncomeDeductionCalculator(dedTables));

        e.給与所得控除後 = (int)r.給与所得控除後の金額;
        e.給与所得控除後調整控除後 = (int)r.給与所得控除後調整後;
        e.所得金額調整控除額 = (int)r.所得金額調整控除;
        e.基礎控除 = (int)r.基礎控除;
        e.配偶者特別控除 = (int)r.配偶者控除;
        e.生保控除 = (int)r.生命保険料控除;
        e.地震控除 = 地震控除;
        e.所得控除計 = (int)r.所得控除額の合計;
        e.課税所得 = (int)r.課税給与所得金額;
        e.所得税 = (int)r.算出所得税額;
        e.年調所得税額 = (int)r.年調所得税額;
        e.年調年税額 = (int)r.年調年税額;
        e.過不足額 = e.源泉計 - e.年調年税額;   // ＋＝過納（還付）、−＝不足（追加徴収）
    }

    /// <summary>
    /// 配偶者・扶養親族の生データ（配偶者扶養タブ）から、年末調整に必要な控除値
    /// （配偶者あり／配偶者所得／配偶者70歳以上／扶養控除等合計／特定親族特別控除額）を自動算定して entry に設定する。
    /// 現行 F_年調_従業員情報 の syogaiayaKoujo / haigusyaSyotoku / chkFuyoKoujo / fuyoKoujo / tokubetuKoujo に一致。
    /// </summary>
    public static void DeriveAutoDeductions(
        YearEndEntry e,
        IncomeTaxYearTables yearTables,
        DependentDeductionTable depTable,
        SpecialRelativeDeductionTable srdTable,
        PensionConversionTable pensionTable,
        int 判定年)
    {
        var pension = new PensionIncomeCalculator(pensionTable);

        // ---- 配偶者 ----
        var sp = e.配偶者;
        bool 配偶者あり = !string.IsNullOrWhiteSpace(sp.氏名);
        DateOnly? 配偶者生年 = ParseDate(sp.生年月日);

        // ---- 所得金額調整控除（子ども・特別障害者等）の自動判定（現行 shotokuTyoseiKoujo）----
        // 給与賞与計>850万 かつ：本人特別障害／共働きフラグ／配偶者が特別障害(収入要件)／23歳未満or特別障害の扶養(収入要件)。
        bool 所調 = e.所得金額調整控除適用;   // 共働きフラグは常に実効
        if (!所調 && e.給与賞与計 > 8_500_000)
        {
            if (To障害(e.障害) == 障害区分.特別障害)
                所調 = true;
            else if (配偶者あり && 配偶者生年 is { } sb && To障害(sp.障害) == 障害区分.特別障害
                     && 収入要件を満たす(sp.収入種類, ParseAmount(sp.収入), sb, pension, 判定年))
                所調 = true;
            else
                所調 = e.扶養親族.Any(d =>
                {
                    if (string.IsNullOrWhiteSpace(d.氏名)) return false;
                    if (ParseDate(d.生年月日) is not { } b) return false;
                    // 23歳未満 or 特別障害、かつ 扶養の収入要件（＝特定親族でない）を満たす。
                    bool 対象 = AgeClassifier.は23歳未満(b, 判定年) || To障害(d.障害) == 障害区分.特別障害;
                    return 対象 && 収入要件を満たす(d.収入種類, d.収入, b, pension, 判定年);
                });
        }
        e.所得金額調整控除実効適用 = 所調;

        // 本人合計所得＝給与所得控除後（調整後）。所得金額調整控除を反映する。寡婦/勤労学生/配偶者控除のしきい値判定に使う。
        decimal 本人所得 = new YearEndAdjustmentCalculator(yearTables)
            .Calculate(e.給与賞与計, 0m, 所調).給与所得控除後調整後;

        decimal 配偶者所得 = 配偶者あり
            ? 収入を所得へ(sp.収入種類, ParseAmount(sp.収入), 配偶者生年, yearTables, pension, 判定年)
            : 0m;
        bool 配偶者70 = 配偶者あり && 配偶者生年 is { } bd && AgeClassifier.は70歳以上(bd, 判定年);

        e.配偶者あり = 配偶者あり;
        e.配偶者所得 = (int)配偶者所得;
        e.配偶者70歳以上 = 配偶者70;

        // ---- 扶養親族（控除対象判定→人的控除合計） ----
        var 控除対象扶養 = new List<扶養親族>();
        var 特定親族たち = new List<特定親族>();
        foreach (var d in e.扶養親族)
        {
            if (string.IsNullOrWhiteSpace(d.氏名)) continue;
            DateOnly? birth = ParseDate(d.生年月日);
            if (birth is not { } b) continue;

            var 障害 = To障害(d.障害);
            bool 一般障害 = 障害 == 障害区分.一般障害;
            bool 特別障害 = 障害 == 障害区分.特別障害;
            bool 留学 = d.非居住者 && d.留学送金 == "1";
            bool 送金 = d.非居住者 && d.留学送金 == "2";

            if (扶養控除対象か(d.収入種類, d.収入, d.同居, b, 一般障害, 特別障害, 留学, 送金, pension, 判定年))
            {
                控除対象扶養.Add(new 扶養親族
                {
                    年齢区分 = AgeClassifier.扶養区分(b, 判定年),
                    同居 = d.同居,
                    障害 = 障害,
                });
            }

            // 特定親族特別控除（19歳以上23歳未満。計算器が年齢・所得/収入の範囲を判定。範囲外は0）。
            特定親族たち.Add(new 特定親族
            {
                生年月日 = b,
                収入種類 = To収入種類(d.収入種類),
                金額 = d.収入,
            });
        }

        var depCalc = new DependentDeductionCalculator(depTable);
        decimal 扶養控除等合計 = depCalc.人的控除合計(
            控除対象扶養,
            配偶者障害: 配偶者あり ? To障害(sp.障害) : 障害区分.なし,
            配偶者同居: sp.同居,
            配偶者合計所得: 配偶者所得,
            本人障害: To障害(e.障害),
            寡婦ひとり親: To寡婦(e.寡婦),
            勤労学生: e.勤労学生,
            本人合計所得: 本人所得);

        e.扶養控除等合計 = (int)扶養控除等合計;
        e.特定親族特別控除額 = (int)new SpecialRelativeDeductionCalculator(srdTable).控除合計(特定親族たち, 判定年);
    }

    /// <summary>
    /// 扶養親族1〜7 の特定親族特別控除額を個別に算定して返す（現行 T_年末調整 列「特定親族N控除」相当・長さ7）。
    /// 源泉徴収票の控除対象親族区分（特定親族区分）判定に使う。氏名/生年月日なし・非該当は 0。
    /// </summary>
    public static int[] 特定親族控除内訳(YearEndEntry e, SpecialRelativeDeductionTable srdTable, int 判定年)
    {
        var calc = new SpecialRelativeDeductionCalculator(srdTable);
        var result = new int[7];
        for (int i = 0; i < 7 && i < e.扶養親族.Length; i++)
        {
            var d = e.扶養親族[i];
            if (string.IsNullOrWhiteSpace(d.氏名)) continue;
            if (ParseDate(d.生年月日) is not { } b) continue;
            result[i] = (int)calc.控除額1人(new 特定親族
            {
                生年月日 = b,
                収入種類 = To収入種類(d.収入種類),
                金額 = d.収入,
            }, 判定年);
        }
        return result;
    }

    /// <summary>
    /// 障害配偶者控除額を算定して返す（現行 T_年末調整 列「障害配偶者控除」相当）。
    /// 配偶者氏名あり＋配偶者合計所得58万円以下＋障害ありのとき 一般27万／特別同居75万／特別別居40万。
    /// 源泉徴収票の障害者数カウント判定（障害配偶者控除≧27万）に使う。
    /// </summary>
    public static int 障害配偶者控除額(YearEndEntry e, DependentDeductionTable depTable)
    {
        if (string.IsNullOrWhiteSpace(e.配偶者.氏名)) return 0;
        return (int)new DependentDeductionCalculator(depTable)
            .障害者控除_配偶者(To障害(e.配偶者.障害), e.配偶者.同居, e.配偶者所得);
    }

    /// <summary>
    /// 扶養親族1〜7 が扶養の収入要件（現行 chkFuyoSyunyu）を満たすかの内訳（長さ7）。
    /// 源泉徴収票の所得金額調整控除の（調整）摘要判定に使う。氏名/生年月日なしは false。
    /// </summary>
    public static bool[] 扶養収入要件内訳(YearEndEntry e, PensionConversionTable pensionTable, int 判定年)
    {
        var pension = new PensionIncomeCalculator(pensionTable);
        var result = new bool[7];
        for (int i = 0; i < 7 && i < e.扶養親族.Length; i++)
        {
            var d = e.扶養親族[i];
            if (string.IsNullOrWhiteSpace(d.氏名)) continue;
            if (ParseDate(d.生年月日) is not { } b) continue;
            result[i] = 収入要件を満たす(d.収入種類, d.収入, b, pension, 判定年);
        }
        return result;
    }

    // ---- 自動算定の補助 ----

    /// <summary>扶養・配偶者の収入要件（現行 chkFuyoSyunyu）：給与 収入≤123万／年金 年金所得≤58万／所得 ≤58万／なし は無条件。</summary>
    private static bool 収入要件を満たす(string 収入種類, decimal 収入, DateOnly birth, PensionIncomeCalculator pension, int 判定年)
        => 収入種類 switch
        {
            "2" => 収入 <= 1_230_000m,
            "3" => pension.年金所得(収入, birth, 判定年) <= 580_000m,
            "4" => 収入 <= 580_000m,
            _ => true,
        };

    /// <summary>収入種類別に控除対象扶養親族かを判定（現行 chkFuyoKoujo＋chkFuyoSyunyu）。</summary>
    private static bool 扶養控除対象か(string 収入種類, decimal 収入, bool 同居, DateOnly birth,
        bool 一般障害, bool 特別障害, bool 留学, bool 送金, PensionIncomeCalculator pension, int 判定年)
    {
        if (!収入要件を満たす(収入種類, 収入, birth, pension, 判定年)) return false;

        if (同居)
            return AgeClassifier.は16歳以上(birth, 判定年) || 一般障害 || 特別障害;

        // 非居住（または未指定）：16歳以上30歳未満／70歳以上／30〜70歳で留学・障害・38万送金のいずれか。
        if (AgeClassifier.は16歳以上(birth, 判定年) && !AgeClassifier.は70歳以上(birth, 判定年)
            && birth >= new DateOnly(判定年 - 30, 1, 2))   // 30歳未満
            return true;
        if (AgeClassifier.は70歳以上(birth, 判定年))
            return true;
        // 30歳以上70歳未満
        bool は30以上70未満 = birth <= new DateOnly(判定年 - 30, 1, 1) && !AgeClassifier.は70歳以上(birth, 判定年);
        return は30以上70未満 && (留学 || 一般障害 || 特別障害 || 送金);
    }

    /// <summary>収入種類別に収入を所得へ換算（給与＝給与所得控除後／年金＝年金所得／所得・なし＝そのまま）。</summary>
    private static decimal 収入を所得へ(string 収入種類, decimal 金額, DateOnly? birth,
        IncomeTaxYearTables yearTables, PensionIncomeCalculator pension, int 判定年)
        => 収入種類 switch
        {
            "2" => 給与所得(金額, yearTables),
            "3" => birth is { } b ? pension.年金所得(金額, b, 判定年) : 0m,
            _ => 金額,
        };

    /// <summary>給与収入→給与所得（給与所得控除後の表引き。4,000円括りは適用しない＝配偶者・扶養親族の所得用）。</summary>
    private static decimal 給与所得(decimal 給与収入, IncomeTaxYearTables yearTables)
    {
        var g = yearTables.給与所得控除後.FirstOrDefault(r => 給与収入 >= r.下限 && 給与収入 < r.上限);
        if (g is null) return 給与収入;
        decimal v = Math.Floor(給与収入 * g.率 + g.加算額);
        return v < 0 ? 0m : v;
    }

    private static 障害区分 To障害(string s) => s switch { "1" => 障害区分.一般障害, "2" => 障害区分.特別障害, _ => 障害区分.なし };
    private static 寡婦ひとり親区分 To寡婦(string s) => s switch { "1" => 寡婦ひとり親区分.寡婦, "2" => 寡婦ひとり親区分.ひとり親, _ => 寡婦ひとり親区分.なし };
    private static 収入種類 To収入種類(string s) => s switch { "2" => 収入種類.給与, "3" => 収入種類.年金, "4" => 収入種類.所得, _ => 収入種類.その他 };

    private static DateOnly? ParseDate(string s)
        => DateOnly.TryParse(s, out var d) ? d : null;

    private static decimal ParseAmount(string s)
        => decimal.TryParse((s ?? "").Replace(",", "").Trim(), out var v) ? v : 0m;

    /// <summary>
    /// 地震保険料控除額を算定する（標準式）。地震保険料は支払額（上限5万）、
    /// 旧長期損害保険料は ≤1万:全額／≤2万:×0.5+5,000／&gt;2万:1.5万。合算上限5万。
    /// </summary>
    public static int 地震保険料控除(int 地震保険, int 長期損害)
    {
        int 地震分 = Math.Min(地震保険, 50000);
        int 長期分 = 長期損害 <= 10000 ? 長期損害
                  : 長期損害 <= 20000 ? 長期損害 / 2 + 5000
                  : 15000;
        return Math.Min(地震分 + 長期分, 50000);
    }
}
