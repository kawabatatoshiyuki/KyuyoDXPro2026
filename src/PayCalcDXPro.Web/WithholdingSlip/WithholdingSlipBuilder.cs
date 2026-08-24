using System.Globalization;
using PayCalcDXPro.Data.Employee;
using PayCalcDXPro.Data.YearEnd;

namespace PayCalcDXPro.Web.WithholdingSlip;

/// <summary>
/// 給与所得の源泉徴収票の「マーカー → 表示値」辞書を組み立てる（現行 R_源泉徴収票.updateReport の移植）。
/// レンダリングは <see cref="PayCalcDXPro.Reporting.WithholdingSlipReport"/> が担い、本クラスは業務ロジックに徹する。
/// データは呼び出し側（LocalDatabaseService）が収集して <see cref="WithholdingSlipInput"/> で渡す。
/// </summary>
public static class WithholdingSlipBuilder
{
    // 現行 MConst 由来のしきい値（源泉徴収票の分岐で使用）。
    private const string 未済 = "2";           // 乙欄="2"
    private const string 乙欄区分 = "3";       // 乙欄="3"
    private const string 障害一般 = "1";
    private const string 障害特別 = "2";
    private const string 寡婦 = "1";
    private const string ひとり親 = "2";
    private const decimal KOJ_RNK_GAK_1 = 10_000_000m;
    private const decimal KOJ_MAE_1 = 11_950_000m;
    private const decimal KOJ_MAE_STKGTSKJ = 12_100_000m;
    private const decimal KOJ_MAE_2 = 1_500_000m;
    private const decimal HIG_STK_MAX = 580_000m;
    private const decimal SYO_HIG_KOJ = 270_000m;
    private const decimal KUY_KOJ_MAX = 5_000_000m;   // 寡婦・ひとり親の所得上限
    private const decimal KUY_KOJ_MIN = 850_000m;     // 勤労学生の所得上限

    public static IReadOnlyDictionary<string, string?> Build(WithholdingSlipInput input)
    {
        var d = new Dictionary<string, string?>();
        var e = input.YearEnd;
        int 判定年 = input.判定年;

        void Put(string marker, object? value)
        {
            if (value is null) return;
            string s = value.ToString() ?? "";
            if (s.Length == 0) return;
            d[marker] = s;
        }

        // ── ヘッダー ──
        Put("**種別", input.種別);
        Put("**年度表示", $"{input.元号} {input.年} 年分");                 // 源泉徴収票：元号 年 年分
        Put("**年度", (ParseInt(input.年) + 1).ToString());                // 給与支払報告書：提出年度＝年分+1
        Put("**（受給者番号）", input.受給者番号);
        if (!string.IsNullOrEmpty(input.役職名) && input.役職名 != "未設定") Put("**（役職名）", input.役職名);
        Put("**氏名_氏名", input.氏名);
        Put("**氏名_（フリガナ）", input.フリガナ);
        Put("**住所", input.住所1 + input.住所2);

        // 本人 個人番号
        SetMyno(d, input.本人番号, "**（個人番号）_個人番号");

        // 支払金額（給与賞与計）
        SetMoney3(d, e.給与賞与計, "支払金額");

        // 社会保険料等：内＝小規模共済、合計＝小規模共済＋申告社保＋給与社保計
        SetZeroCheck(d, e.小規模共済, "**社会保険料等の金額_内");
        SetMoney2(d, e.小規模共済 + e.申告社保 + e.給与社保計, "社会保険料等の金額");

        bool 乙 = e.乙欄 == 未済 || e.乙欄 == 乙欄区分;
        if (乙)
        {
            // 未済・乙欄：源泉徴収額＝給与源泉計＋賞与源泉計
            SetMoney3(d, e.給与源泉計 + e.賞与源泉計, "源泉徴収額");

            // 控除対象配偶者（源泉・特別）
            if (((e.所得金額調整控除額 > 0 && e.給与賞与計 <= KOJ_MAE_STKGTSKJ) || e.給与賞与計 <= KOJ_MAE_1)
                && ParseInt(e.配偶者.収入) <= KOJ_MAE_2 && !string.IsNullOrEmpty(e.配偶者.氏名))
            {
                Put("**(源泉・特別)控除対象配偶者_（フリガナ）", e.配偶者.フリガナ);
                Put("**(源泉・特別)控除対象配偶者_氏名", e.配偶者.氏名);
                SetMoneyComma(d, e.配偶者所得, "**(源泉・特別)控除対象配偶者_配偶者の合計所得_円");
                SetMyno(d, input.配偶者番号, "**(源泉・特別)控除対象配偶者_個人番号_個人番号");
                if (e.配偶者.非居住者) Put("**(源泉・特別)控除対象配偶者_区分", "○");
                Put("**(源泉)控除対象配偶者の有無等_有", "○");
                if (Is70(e.配偶者.生年月日, 判定年)) Put("**老人", "○");
            }
        }
        else
        {
            SetMoney3(d, e.特定親族特別控除額, "特定親族特別控除額");
            SetMoney3(d, e.給与所得控除後調整控除後, "給与所得控除後の金額(調整控除後)");
            SetMoney3(d, e.所得控除計, "所得控除の額の合計額");
            SetMoney3(d, e.年調年税額, "源泉徴収額");
            SetMoney2(d, e.生保控除, "生命保険料の控除額");
            SetMoney2(d, e.地震控除, "地震保険料の控除額");
            SetMoney2(d, e.配偶者特別控除, "配偶者(特別)控除の額");
            // 住宅借入金等特別控除の額（住宅控除が所得税を超える場合は所得税を上限に）
            SetMoney2(d, e.住宅控除 > e.所得税 ? e.所得税 : e.住宅控除, "住宅借入金等特別控除の額");

            // 生命保険料の金額の内訳（金額はカンマ編集）
            SetMoneyComma(d, e.新生命保険, "**生命保険料の金額の内訳_新生命保険料の金額_円");
            SetMoneyComma(d, e.生命保険, "**生命保険料の金額の内訳_旧生命保険料の金額_円");
            SetMoneyComma(d, e.介護医療保険, "**生命保険料の金額の内訳_介護医療保険料の金額_円");
            SetMoneyComma(d, e.新個人年金, "**生命保険料の金額の内訳_新個人年金保険料の金額_円");
            SetMoneyComma(d, e.個人年金, "**生命保険料の金額の内訳_旧個人年金保険料の金額_円");

            // 住宅借入金等特別控除の内訳（金額はカンマ編集・適用数は件数のまま）
            int jutaku = 0;
            if (!string.IsNullOrEmpty(e.新築購入居住開始)) jutaku++;
            if (!string.IsNullOrEmpty(e.増改築居住開始)) jutaku++;
            SetZeroCheck(d, jutaku, "**住宅借入金等特別控除の額の内訳_住宅借入金等特別控除適用数");
            SetMoneyComma(d, e.住宅借入金特別控除可能額, "**住宅借入金等特別控除の額の内訳_住宅借入金等特別控除可能額_円");
            SetMoneyComma(d, e.国民年金保険料等, "**(源泉・特別)控除対象配偶者_国民年金保険料等の金額_円");
            SetMoneyComma(d, e.基礎控除, "**(源泉・特別)控除対象配偶者_基礎控除の額");
            SetMoneyComma(d, e.長期損害, "**(源泉・特別)控除対象配偶者_旧長期損害保険料の金額_円");
            SetMoneyComma(d, e.所得金額調整控除額, "**(源泉・特別)控除対象配偶者_所得金額調整控除額");

            // 居住開始年月日（1回目＝新築購入／2回目＝増改築）
            if (TryDate(e.新築購入居住開始, out var d1))
            {
                Put("**住宅借入金等特別控除の額の内訳_居住開始年月日（１回目）_年", Wareki年(d1));
                Put("**住宅借入金等特別控除の額の内訳_居住開始年月日（１回目）_月", d1.Month);
                Put("**住宅借入金等特別控除の額の内訳_居住開始年月日（１回目）_日", d1.Day);
                Put("**住宅借入金等特別控除の額の内訳_住宅借入金等特別控除区分(1回目)", input.新築購入区分名);
                SetMoneyComma(d, e.新築購入借入金, "**住宅借入金等特別控除の額の内訳_住宅借入金等年末残高(1回目)_円");
            }
            if (TryDate(e.増改築居住開始, out var d2))
            {
                Put("**住宅借入金等特別控除の額の内訳_居住開始年月日（２回目）_年", Wareki年(d2));
                Put("**住宅借入金等特別控除の額の内訳_居住開始年月日（２回目）_月", d2.Month);
                Put("**住宅借入金等特別控除の額の内訳_居住開始年月日（２回目）_日", d2.Day);
                Put("**住宅借入金等特別控除の額の内訳_住宅借入金等特別控除区分(2回目)", input.増改築区分名);
                SetMoneyComma(d, e.増改築借入金, "**住宅借入金等特別控除の額の内訳_住宅借入金等年末残高(2回目)_円");
            }

            // 控除対象配偶者（配偶者特別控除ありの場合）
            if (e.配偶者特別控除 > 0 && !string.IsNullOrEmpty(e.配偶者.氏名))
            {
                Put("**(源泉・特別)控除対象配偶者_（フリガナ）", e.配偶者.フリガナ);
                Put("**(源泉・特別)控除対象配偶者_氏名", e.配偶者.氏名);
                SetMoneyComma(d, e.配偶者所得, "**(源泉・特別)控除対象配偶者_配偶者の合計所得_円");
                SetMyno(d, input.配偶者番号, "**(源泉・特別)控除対象配偶者_個人番号_個人番号");
                if (e.配偶者.非居住者) Put("**(源泉・特別)控除対象配偶者_区分", "○");
            }

            // (源泉)控除対象配偶者の有無等＿有／老人
            if (!string.IsNullOrEmpty(e.配偶者.氏名)
                && e.給与所得控除後調整控除後 <= KOJ_RNK_GAK_1 && e.配偶者所得 <= HIG_STK_MAX
                && (e.給与所得控除後調整控除後 != 0 || e.配偶者所得 != 0))
            {
                Put("**(源泉)控除対象配偶者の有無等_有", "○");
                if (Is70(e.配偶者.生年月日, 判定年)) Put("**老人", "○");
            }
        }

        // ── 摘要3行目：前段（同配／所得調整の調整）＋扶養親族5人目以降 ──
        string tekiyou3 = BuildTekiyouFront(input) + BuildDependents(d, input);

        // ── 本人の各種区分（チェック） ──
        if (e.未成年) Put("**未成年者", "○");
        if (e.外国人) Put("**外国人", "○");
        if (e.死亡退職) Put("**死亡退職", "○");
        if (e.災害者) Put("**災害者", "○");
        if (e.乙欄 == 乙欄区分) Put("**乙欄", "○");
        if (e.障害 == 障害一般) Put("**本人が障害者_その他", "○");
        else if (e.障害 == 障害特別) Put("**本人が障害者_特別", "○");
        if (e.給与所得控除後調整控除後 <= KUY_KOJ_MAX)
        {
            if (e.寡婦 == 寡婦) Put("**寡婦", "○");
            else if (e.寡婦 == ひとり親) Put("**ひとり親", "○");
        }
        if (e.給与所得控除後調整控除後 <= KUY_KOJ_MIN && e.勤労学生) Put("**勤労学生", "○");

        // ── 中途就・退職 ──
        BuildMidYear(d, e);

        // ── 受給者生年月日 ──
        if (TryDate(input.生年月日, out var birth))
        {
            Put("**受給者生年月日_元号", Wareki元号(birth));
            Put("**受給者生年月日_年", Wareki年(birth));
            Put("**受給者生年月日_月", birth.Month);
            Put("**受給者生年月日_日", birth.Day);
        }

        // ── 摘要（扶養親族の続き＋前社・追記文） ──
        BuildTekiyou(d, input, tekiyou3);

        // ── 支払者（会社） ──
        Put("**支払者_氏名又は名称_氏名又は名称", input.会社名);
        Put("**支払者_住所(居所)又は所在地", input.会社所在地);
        Put("**支払者_氏名又は名称_（電話）", input.会社電話);
        // 事業所マイナンバー：右詰めで 番号13..（末尾から）へ
        string zig = input.事業所番号 ?? "";
        for (int j = 0; j < zig.Length; j++)
            Put($"**支払者_個人番号又は法人番号_番号{13 - j}", zig[zig.Length - 1 - j].ToString());

        return d;
    }

    // ── 扶養親族（控除対象1-4／16歳未満1-4）＋カウント。特定親族控除区分・非居住者区分の詳細は次段で拡充。 ──
    // 摘要3行目に積む文字列を返す。
    private static string BuildDependents(Dictionary<string, string?> d, WithholdingSlipInput input)
    {
        var e = input.YearEnd;
        int 判定年 = input.判定年;
        int sinzoku = 1, sinzoku16 = 1;
        string sinzokuMyno = "";
        string sinzokuMyno16 = "";   // 給与支払報告書用：16歳未満5人目以降の個人番号（源泉徴収票では未使用）
        string tekiyou3 = "";

        for (int j = 0; j < 7; j++)
        {
            var f = e.扶養親族[j];
            if (string.IsNullOrEmpty(f.氏名)) continue;
            string? myno = input.扶養番号[j];

            if (Is16以上(f.生年月日, 判定年))
            {
                int 特定控除 = input.特定親族控除[j];
                if (sinzoku < 5)
                {
                    d[$"**控除対象扶養親族_{sinzoku}_（フリガナ）"] = f.フリガナ;
                    d[$"**控除対象扶養親族_{sinzoku}_氏名"] = f.氏名;
                    // 控除対象親族区分：非居住者は詳細区分(01-04)、特定親族控除ありは特定親族区分(10-91)で上書き
                    if (f.非居住者)
                    {
                        string kbn = SetHikyojyushaKbn(f, 判定年);
                        if (特定控除 != 0) kbn = SetTokuteiKbn(2, 特定控除);
                        if (kbn.Length > 0) d[$"**控除対象扶養親族_{sinzoku}_区分"] = kbn;
                    }
                    else if (f.同居 && 特定控除 != 0)
                    {
                        string kbn = SetTokuteiKbn(1, 特定控除);
                        if (kbn.Length > 0) d[$"**控除対象扶養親族_{sinzoku}_区分"] = kbn;
                    }
                    SetMyno(d, myno, $"**控除対象扶養親族_{sinzoku}_個人番号_個人番号");
                }
                else if (e.乙欄 != 未済)
                {
                    sinzokuMyno += $"({sinzoku - 4})\n{myno}\n";
                    tekiyou3 += $"({sinzoku - 4}){f.氏名}";
                    // 5人目以降は区分を摘要（備考）に括弧書きで追記
                    if (特定控除 != 0)
                    {
                        if (f.非居住者) tekiyou3 += $"（{SetTokuteiKbn(2, 特定控除)}）";
                        else if (f.同居) tekiyou3 += $"（{SetTokuteiKbn(1, 特定控除)}）";
                    }
                    else if (f.非居住者)
                    {
                        string kbn = SetHikyojyushaKbn(f, 判定年);
                        if (kbn.Length > 0) tekiyou3 += $"（{kbn}）";
                    }
                }
                sinzoku++;
            }
            else
            {
                if (sinzoku16 < 5)
                {
                    d[$"**１６歳未満の扶養親族_{sinzoku16}_（フリガナ）"] = f.フリガナ;
                    d[$"**１６歳未満の扶養親族_{sinzoku16}_氏名"] = f.氏名;
                    if (f.非居住者) d[$"**１６歳未満の扶養親族_{sinzoku16}_区分"] = "○";
                    SetMyno(d, myno, $"**１６歳未満の扶養親族_{sinzoku16}_個人番号_個人番号");
                }
                else if (e.乙欄 != 未済)
                {
                    // 給与支払報告書は16歳未満5人目以降の個人番号も別欄に記録（源泉徴収票は摘要のみ）
                    sinzokuMyno16 += $"({sinzoku16 - 4})\n{myno}\n";
                    tekiyou3 += $"({sinzoku16 - 4}){f.氏名}（年少）";
                    if (f.非居住者) tekiyou3 += "（非居住者）";
                }
                sinzoku16++;
            }
        }
        // 源泉徴収票は備考欄1つ、給与支払報告書は控除対象/16歳未満で個人番号欄が分かれる（テンプレに無いマーカーは無視される）。
        if (!string.IsNullOrEmpty(sinzokuMyno))
        {
            d["**扶養親族_（備考）"] = sinzokuMyno;                          // 源泉徴収票
            d["**5人目以降の控除対象扶養親族の個人番号"] = sinzokuMyno;       // 給与支払報告書
        }
        if (!string.IsNullOrEmpty(sinzokuMyno16))
            d["**5人目以降の16歳未満の扶養親族等の個人番号"] = sinzokuMyno16;  // 給与支払報告書

        // 乙欄="2"(未済) 以外のときのみカウントを出力
        if (e.乙欄 != 未済)
        {
            int 特定 = 0, 老人内 = 0, 老人人 = 0, その他 = 0, 特親 = 0, 未満16 = 0;
            int 障特別内 = 0, 障特別人 = 0, 障その他 = 0, 非居住 = 0;

            // 配偶者の障害／非居住カウント
            if (!string.IsNullOrEmpty(e.配偶者.氏名))
            {
                if (e.配偶者.障害 == 障害特別 && input.障害配偶者控除 >= SYO_HIG_KOJ)
                {
                    障特別人++;
                    if (e.配偶者.同居) 障特別内++;
                }
                if (e.配偶者.障害 == 障害一般 && input.障害配偶者控除 >= SYO_HIG_KOJ) 障その他++;
                if (e.配偶者.非居住者) 非居住++;
            }

            for (int j = 0; j < 7; j++)
            {
                var f = e.扶養親族[j];
                if (string.IsNullOrEmpty(f.氏名) || !TryDate(f.生年月日, out var bd)) continue;
                int 特定控除 = input.特定親族控除[j];
                if (Is19以上23未満(f.生年月日, 判定年) && 特定控除 == 0) 特定++;
                else if (特定控除 > 0) 特親++;
                if (Is70(f.生年月日, 判定年)) { 老人人++; if (f.同居) 老人内++; }
                if (Is23以上70未満(f.生年月日, 判定年) || Is16以上19未満(f.生年月日, 判定年)) その他++;
                if (Is16未満(f.生年月日, 判定年)) 未満16++;
                if (f.障害 == 障害特別) { 障特別人++; if (f.同居) 障特別内++; }
                if (f.障害 == 障害一般) 障その他++;
                if (f.非居住者) 非居住++;
            }

            SetZeroCheck(d, 特定, "**控除対象扶養親族の数（配偶者を除く 。）_特定_人");
            SetZeroCheck(d, 老人内, "**控除対象扶養親族の数（配偶者を除く 。）_老人_内");
            SetZeroCheck(d, 老人人, "**控除対象扶養親族の数（配偶者を除く 。）_老人_人");
            SetZeroCheck(d, その他, "**控除対象扶養親族の数（配偶者を除く 。）_その他_人");
            SetZeroCheck(d, 特親, "**特定親族の数_人");
            SetZeroCheck(d, 未満16, "**16歳未満扶養親族の数_人");
            SetZeroCheck(d, 障特別内, "**障害者の数（本人を除く。）_特別_内");
            SetZeroCheck(d, 障特別人, "**障害者の数（本人を除く。）_特別_人");
            SetZeroCheck(d, 障その他, "**障害者の数（本人を除く。）_その他_人");
            SetZeroCheck(d, 非居住, "**非居住者である親族の数_人");
        }
        else
        {
            d["**（摘要）_1行目"] = "年調未済";
        }

        return tekiyou3;
    }

    private static void BuildMidYear(Dictionary<string, string?> d, YearEndEntry e)
    {
        void 就(DateTime dt) { d["**中途就・退職_就職"] = "○"; d["**中途就・退職_年"] = Wareki年(dt); d["**中途就・退職_月"] = dt.Month.ToString(); d["**中途就・退職_日"] = dt.Day.ToString(); }
        bool 就あり = e.就職 && TryDate(e.中途就職日, out _);
        bool 退あり = e.退職 && TryDate(e.中途退職日, out _);
        if (e.就職 && e.退職)
        {
            d["**中途就・退職_就職"] = "○"; d["**中途就・退職_退職"] = "○";
            if (TryDate(e.中途就職日, out var s) && TryDate(e.中途退職日, out var r))
            {
                d["**中途就・退職_年"] = $"{Wareki年(s)}\n{Wareki年(r)}";
                d["**中途就・退職_月"] = $"{s.Month}\n{r.Month}";
                d["**中途就・退職_日"] = $"{s.Day}\n{r.Day}";
            }
        }
        else if (就あり) { TryDate(e.中途就職日, out var s); 就(s); }
        else if (退あり) { TryDate(e.中途退職日, out var r); d["**中途就・退職_退職"] = "○"; d["**中途就・退職_年"] = Wareki年(r); d["**中途就・退職_月"] = r.Month.ToString(); d["**中途就・退職_日"] = r.Day.ToString(); }
    }

    // ── 摘要3行目の前段（現行 R_源泉徴収票 772-824）。同配／所得調整の（調整）を積む。乙欄（未済/乙欄区分）は非対象。 ──
    private static string BuildTekiyouFront(WithholdingSlipInput input)
    {
        var e = input.YearEnd;
        int 判定年 = input.判定年;
        if (e.乙欄 == 未済 || e.乙欄 == 乙欄区分) return "";

        string t = "";
        if (e.所得金額調整控除額 != 0)
        {
            // 配偶者が特別障害（合計所得58万以下）→（同配）
            if (!string.IsNullOrEmpty(e.配偶者.氏名) && e.配偶者所得 <= HIG_STK_MAX && e.配偶者.障害 == 障害特別)
                t += e.配偶者.氏名 + "（同配）";

            // 扶養親族：特定親族控除なし＋収入要件＋（23歳未満 or 23歳以上かつ特別障害）→（調整）
            for (int j = 0; j < 7; j++)
            {
                var f = e.扶養親族[j];
                if (string.IsNullOrEmpty(f.氏名)) continue;
                if (input.特定親族控除[j] != 0 || !input.扶養収入要件[j]) continue;
                if (未満(f.生年月日, 判定年, 23))
                    t += f.氏名 + "（調整）";
                else if (以上(f.生年月日, 判定年, 23) && f.障害 == 障害特別)
                    t += f.氏名 + "（調整）";
            }
        }
        else
        {
            // 給与所得控除後（調整後）>1000万＋配偶者障害（合計所得58万以下）→（同配）
            if (!string.IsNullOrEmpty(e.配偶者.氏名) && e.給与所得控除後調整控除後 > KOJ_RNK_GAK_1
                && e.配偶者所得 <= HIG_STK_MAX && (e.配偶者.障害 == 障害一般 || e.配偶者.障害 == 障害特別))
                t += e.配偶者.氏名 + "（同配）";
        }

        // 所得金額調整控除の適用扶養者（調整）
        if (e.所得金額調整控除適用 && !string.IsNullOrEmpty(input.所得金額調整控除適用の扶養者氏名))
            t += input.所得金額調整控除適用の扶養者氏名 + "（調整）";

        return t;
    }

    private static void BuildTekiyou(Dictionary<string, string?> d, WithholdingSlipInput input, string tekiyou3)
    {
        var e = input.YearEnd;
        string tekiyou4 = "";

        if (e.前社収入 != 0)
        {
            tekiyou4 = $"{e.所在地} {e.前社の名称}";
            d["**（摘要）_5行目"] = $"支払金額　{e.前社収入:N0}円 徴収金額　{e.前社源泉:N0}円 社会保険料　{e.前社社保:N0}円";
            if (!string.IsNullOrEmpty(e.前社退職日)) d["**（摘要）_6行目"] = e.前社退職日 + "退職";
        }
        if (!string.IsNullOrEmpty(tekiyou3)) d["**（摘要）_3行目"] = tekiyou3;
        if (!string.IsNullOrEmpty(tekiyou4)) d["**（摘要）_4行目"] = tekiyou4;

        // 追記文の配置（現行ロジック）
        string add = e.源泉徴収票摘要欄追記文 ?? "";
        if (add.Length > 0)
        {
            if (tekiyou4 == "")
            {
                if (tekiyou3 == "") d["**（摘要）_3行目"] = add;
                else d["**（摘要）_4行目"] = add;
            }
            else d["**（摘要）_7行目"] = add;
        }
    }

    // ── 金額の桁分割（現行 setMoney_3cell / setMoney_2cell） ──
    private static void SetMoney3(Dictionary<string, string?> d, int value, string col)
    {
        string s = value.ToString(CultureInfo.InvariantCulture).PadLeft(7, ' ');
        if (s == "       ") { d[$"**{col}_円"] = "0"; return; }
        d[$"**{col}_円"] = Trim(s.Substring(s.Length - 3));
        d[$"**{col}_千"] = Trim(s.Substring(s.Length - 6, 3));
        d[$"**{col}_内"] = Trim(s.Substring(0, s.Length - 6));
    }

    private static void SetMoney2(Dictionary<string, string?> d, int value, string col)
    {
        string s = value.ToString(CultureInfo.InvariantCulture).PadLeft(6, ' ');
        if ((s == "      " || s == "     0") && col == "社会保険料等の金額") { d[$"**{col}_円"] = "0"; return; }
        if (s == "     0") { return; }  // 0 は空欄
        d[$"**{col}_円"] = Trim(s.Substring(s.Length - 3));
        d[$"**{col}_千"] = Trim(s.Substring(0, s.Length - 3));
    }

    private static void SetZeroCheck(Dictionary<string, string?> d, int value, string marker)
    {
        if (value != 0) d[marker] = value.ToString(CultureInfo.InvariantCulture);
    }

    // 0以外のときカンマ区切りで出力（金額欄用）。
    private static void SetMoneyComma(Dictionary<string, string?> d, int value, string marker)
    {
        if (value != 0) d[marker] = value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static void SetMyno(Dictionary<string, string?> d, string? number, string prefix)
    {
        number ??= "";
        if (number.Length < 12) return;
        for (int j = 0; j < 12; j++) d[$"{prefix}{j + 1}"] = number[j].ToString();
    }

    // ── 控除対象扶養親族の区分コード（現行 setTokuteiKbn / setHikyojyushaKbn） ──

    // 特定親族の控除区分コード（現行 setTokuteiKbn）。residenceStatus: 1=同居, 2=非居住。
    // 十の位＝特定親族控除額の階級、一の位＝0(同居)/1(非居住)。控除額が階級外なら空。
    private static string SetTokuteiKbn(int residenceStatus, int 特定親族控除額)
    {
        string tens = 特定親族控除額 switch
        {
            630000 => "1", // 58万円超85万円以下：63万円
            610000 => "2", // 85万円超90万円以下：61万円
            510000 => "3", // 90万円超95万円以下：51万円
            410000 => "4", // 95万円超100万円以下：41万円
            310000 => "5", // 100万円超105万円以下：31万円
            210000 => "6", // 105万円超110万円以下：21万円
            110000 => "7", // 110万円超115万円以下：11万円
            60000 => "8",  // 115万円超120万円以下：6万円
            30000 => "9",  // 120万円超123万円以下：3万円
            _ => "",
        };
        if (tens.Length == 0) return "";
        return tens + (residenceStatus == 2 ? "1" : "0");
    }

    // 非居住者親族の控除対象扶養親族区分コード（現行 setHikyojyushaKbn）。
    // 16歳以上30歳未満 又は 70歳以上→"01"。30歳以上70歳未満で留学→"02"／障害→"03"／38万以上送金→"04"。
    private static string SetHikyojyushaKbn(YearEndDependent f, int 判定年)
    {
        if ((以上(f.生年月日, 判定年, 16) && 未満(f.生年月日, 判定年, 30)) || 以上(f.生年月日, 判定年, 70))
            return "01";
        if (f.留学送金 == "1") return "02";
        if (f.障害 == 障害一般 || f.障害 == 障害特別) return "03";
        if (f.留学送金 == "2") return "04";
        return "";
    }

    private static string? Trim(string s) { s = s.Trim(); return s.Length == 0 ? null : s; }
    private static string? Nz(int v) => v == 0 ? null : v.ToString(CultureInfo.InvariantCulture);
    private static int ParseInt(string? s) => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    // ── 和暦・年齢ヘルパー ──
    private static readonly CultureInfo Ja = MakeJa();
    private static CultureInfo MakeJa()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("ja-JP").Clone();
        c.DateTimeFormat.Calendar = new JapaneseCalendar();
        return c;
    }
    private static string Wareki年(DateTime dt) => dt.ToString("yy", Ja).TrimStart('0');
    private static string Wareki元号(DateTime dt) => dt.ToString("gg", Ja).TrimStart('0');

    private static bool TryDate(string? s, out DateTime dt)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);

    // 年齢境界（現行 chkNenrei と一致）：「以上」＝生年月日 ≦ (判定年−年齢)/1/1、「未満」＝生年月日 ≧ (判定年−年齢)/1/2。
    private static bool 以上(string? ymd, int 判定年, int 年齢)
        => TryDate(ymd, out var dt) && DateOnly.FromDateTime(dt) <= new DateOnly(判定年 - 年齢, 1, 1);
    private static bool 未満(string? ymd, int 判定年, int 年齢)
        => TryDate(ymd, out var dt) && DateOnly.FromDateTime(dt) >= new DateOnly(判定年 - 年齢, 1, 2);
    private static bool Is70(string? ymd, int y) => 以上(ymd, y, 70);
    private static bool Is16以上(string? ymd, int y) => 以上(ymd, y, 16);
    private static bool Is16未満(string? ymd, int y) => 未満(ymd, y, 16);
    private static bool Is19以上23未満(string? ymd, int y) => 以上(ymd, y, 19) && 未満(ymd, y, 23);
    private static bool Is23以上70未満(string? ymd, int y) => 以上(ymd, y, 23) && 未満(ymd, y, 70);
    private static bool Is16以上19未満(string? ymd, int y) => 以上(ymd, y, 16) && 未満(ymd, y, 19);
}
