using System.Globalization;
using PayCalcDXPro.Data.YearEnd;
using PayCalcDXPro.Reporting;

namespace PayCalcDXPro.Web.WithholdingSlip;

/// <summary>
/// 給与所得に対する源泉徴収簿の「マーカー → 表示値」辞書を組み立てる（現行 R_徴収簿.createReport の移植）。
/// レンダリングは <see cref="WithholdingLedgerReport"/> が担い、本クラスは業務ロジックに徹する。
///
/// 移植方針：
///  ・定額減税欄(㉔-2〜㉔-4)は省略（ブラウザ版に定額減税なし。源泉徴収票ビルダーと同方針）。
///  ・丸は「甲/乙」（乙欄）と「過納/不足」（過不足額）のみ実装。配偶者・寡婦・勤労学生の丸は対象外。
///  ・扶養控除等の申告の各カウントは、現行の従業員マスタ集計列ではなく 扶養親族[] から年齢・障害で再集計する
///    （源泉徴収票ビルダーと同じ年齢境界ロジック）。
/// </summary>
public static class WithholdingLedgerBuilder
{
    // 現行 MConst 由来のコード。
    private const string 乙欄区分 = "3";       // 乙欄="3"→乙
    private const string 最後の賞与調整 = "3";  // 年末調整方法="3"→過不足を賞与側へ
    private const string 障害一般 = "1";
    private const string 障害特別 = "2";

    public static IReadOnlyDictionary<string, string?> Build(WithholdingLedgerInput input)
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
        // 金額：0以外のときカンマ編集（テンプレのセル書式に相当）。
        void Money(string marker, int value) { if (value != 0) d[marker] = value.ToString("N0", CultureInfo.InvariantCulture); }
        // 件数：0以外のときのみ（源泉徴収票と同じく0は空欄）。
        void Num(string marker, int value) { if (value != 0) d[marker] = value.ToString(CultureInfo.InvariantCulture); }

        // ── ヘッダー ──
        if (!string.IsNullOrEmpty(input.所属) && input.所属 != "未設定") Put("**所属", input.所属);
        if (!string.IsNullOrEmpty(input.職名) && input.職名 != "未設定") Put("**職名", input.職名);
        Put("**住所_郵便番号", input.郵便番号);
        Put("**住所_住所", input.住所);
        Put("**氏名_氏名", input.氏名);
        Put("**氏名_フリガナ", input.フリガナ);
        if (TryDate(input.生年月日, out var birth))
            Put("**氏名_生年月日", $"{Wareki元号(birth)}{Wareki年(birth)}年{birth.Month}月{birth.Day}日");
        Put("**整理番号", input.整理番号);

        // ── 前職分 ──
        Money("**前職分_前社給与", e.前社収入);
        Money("**前職分_前社社保", e.前社社保);
        int 給控後計 = e.前社収入 - e.前社社保;
        Money("**前職分_前社会保険料控除後の給与等の金額", 給控後計);
        Money("**前職分_前社源泉", e.前社源泉);

        // ── 給料・手当等（月別 1〜12） ──
        for (int j = 0; j < 12; j++)
        {
            if (e.給与[j] == 0 && e.社保[j] == 0) continue;
            string p = $"**給料・手当等_月区分{j + 1}_";
            Put(p + "支給月", input.支給月[j]);
            Put(p + "支給日", input.支給日[j]);
            Money(p + "総支給金額", e.給与[j]);
            Money(p + "社会保険料の控除額", e.社保[j]);
            int 控後 = e.給与[j] - e.社保[j];
            Money(p + "社会保険料控除後の給与等の金額", 控後);
            Num(p + "扶養親族等の数", e.給与扶養親族数[j]);
            Money(p + "算出税額", e.源泉[j]);
            Money(p + "差引徴収税額", e.源泉[j]);
            給控後計 += 控後;
        }
        Money("**給料・手当等_計_総支給金額", e.給与計);
        Money("**給料・手当等_計_社会保険料の控除額", e.給与社保計);
        Money("**給料・手当等_計_社会保険料控除後の給与等の金額", 給控後計);
        Money("**給料・手当等_計_算出税額", e.給与源泉計);

        // ── 賞与等（1〜4） ──
        int 賞控後計 = 0;
        for (int j = 0; j < 4; j++)
        {
            if (e.賞与[j] == 0) continue;
            string p = $"**賞与等_賞与等{j + 1}_";
            Put(p + "月区分", input.賞与支給月[j]);
            Put(p + "支給月", input.賞与支給月[j]);
            Put(p + "支給日", input.賞与支給日[j]);
            Money(p + "総支給金額", e.賞与[j]);
            Money(p + "社会保険料の控除額", e.社保賞与[j]);
            int 控後 = e.賞与[j] - e.社保賞与[j];
            Money(p + "社会保険料控除後の給与等の金額", 控後);
            Num(p + "扶養親族等の数", e.賞与扶養親族数[j]);
            Money(p + "算出税額", e.源泉賞与[j]);
            賞控後計 += 控後;
        }
        Money("**賞与等_計_総支給金額", e.賞与計);
        Money("**賞与等_計_社会保険料の控除額", e.賞与社保計);
        Money("**賞与等_計_社会保険料控除後の給与等の金額", 賞控後計);
        Money("**賞与等_計_算出税額", e.賞与源泉計);

        // ── 年末調整による過不足税額（年末調整方法により給与側／賞与側へ）──
        // 過不足額：browser では ＋＝過納（還付）／−＝不足（追加徴収）。計行には符号付きで置く。
        if (input.年末調整方法 != 最後の賞与調整)
        {
            Money("**給料・手当等_計_年末調整による過不足税額", e.過不足額);
            Money("**給料・手当等_計_差引徴収税額", e.過不足額);
        }
        else
        {
            Money("**賞与等_計_年末調整による過不足税額", e.過不足額);
            Money("**賞与等_計_差引徴収税額", e.過不足額);
        }

        // ── 扶養控除等の申告（扶養親族[] から年齢・障害で再集計） ──
        BuildShinkoku(d, input, Num);

        // ── 年末調整（計算結果） ──
        Money("**年末調整_給料･手当等_金額", e.給与計);
        Money("**年末調整_給料･手当等_税額", e.給与源泉計);
        Money("**年末調整_賞与等_金額", e.賞与計);
        Money("**年末調整_賞与等_税額", e.賞与源泉計);
        Money("**年末調整_計_金額", e.給与賞与計);
        Money("**年末調整_計_税額", e.源泉計);
        Money("**年末調整_給与所得控除後の給与等の金額", e.給与所得控除後);
        Money("**年末調整_所得金額調整控除額", e.所得金額調整控除額);
        Money("**年末調整_給与所得控除後の給与等の金額(調整控除後)", e.給与所得控除後調整控除後);
        Money("**年末調整_社会保険料等控除額_給与等からの控除分（②＋⑤）", e.給与社保計 + e.賞与社保計);
        Money("**年末調整_社会保険料等控除額_申告による社会保険料の控除分", e.申告社保);
        Money("**年末調整_社会保険料等控除額_申告による小規模企業共済等掛金の控除分", e.小規模共済);
        Money("**年末調整_生命保険料の控除額", e.生保控除);
        Money("**年末調整_地震保険料の控除額", e.地震控除);
        Money("**年末調整_配偶者(特別)控除額", e.配偶者特別控除);
        Money("**年末調整_特定親族特別控除額", e.特定親族特別控除額);
        Money("**年末調整_扶養控除額及び障害者等の控除額の合計額", e.扶養控除等合計);
        Money("**年末調整_基礎控除額", e.基礎控除);
        Money("**年末調整_所得控除額の合計額", e.所得控除計);
        Money("**_差引課税給与所得金額(⑪-⑳)及び算出年税額_金額", e.課税所得);
        Money("**_差引課税給与所得金額(⑪-⑳)及び算出年税額_税額", e.所得税);
        Money("**年末調整_（特定増改築等）住宅借入金等特別控除額", e.住宅控除);
        Money("**年末調整_年調所得税額", e.年調所得税額);
        Money("**年末調整_年調年税額", e.年調年税額);
        Money("**年末調整_差引超過額又は不足額", Math.Abs(e.過不足額));
        Money("**年末調整_所得金額調整控除の適用_配偶者の合計所得金額", e.配偶者所得);
        Money("**年末調整_所得金額調整控除の適用_旧長期損害保険料支払額", e.長期損害);
        Money("**年末調整_所得金額調整控除の適用_⑫のうち小規模企業共済等掛金の金額", e.小規模共済);
        Money("**年末調整_所得金額調整控除の適用_⑬のうち国民年金保険料等の金額", e.国民年金保険料等);
        Put("**年末調整_所得金額調整控除の適用", e.所得金額調整控除額 > 0 ? "有" : "無");

        // 非課税となる通勤手当（値ありのとき）
        if (e.非課税通勤手当金額 != 0)
        {
            Put("**年末調整_非課税となる通勤手当", "非課税となる通勤手当");
            Money("**年末調整_金額", e.非課税通勤手当金額);
            Put("**年末調整_円", "円");
            Put("**年末調整_計算根拠", e.非課税通勤手当算出根拠);
        }

        // ── 年度・年 ──
        Put("**年度", input.元号);
        Put("**年", input.年);

        // ── 丸（甲/乙・過納/不足のみ） ──
        if (e.乙欄 == 乙欄区分) d[WithholdingLedgerReport.丸_乙] = "1";
        else d[WithholdingLedgerReport.丸_甲] = "1";
        if (e.過不足額 > 0) d[WithholdingLedgerReport.丸_過納] = "1";      // 過納（還付）
        else if (e.過不足額 < 0) d[WithholdingLedgerReport.丸_不足] = "1"; // 不足（追加徴収）

        return d;
    }

    // ── 扶養控除等の申告：扶養親族[]＋本人／配偶者の障害から再集計（源泉徴収票と同じ年齢境界） ──
    private static void BuildShinkoku(Dictionary<string, string?> d, WithholdingLedgerInput input, Action<string, int> Num)
    {
        var e = input.YearEnd;
        int 判定年 = input.判定年;

        int 一般 = 0, 特定 = 0, 特親 = 0, 老人内 = 0, 老人人 = 0;
        int 障一般 = 0, 障特別 = 0, 障同居特別 = 0;

        // 本人の障害
        if (e.障害 == 障害一般) 障一般++;
        else if (e.障害 == 障害特別) 障特別++;

        // 配偶者の障害（配偶者が存在するとき）
        if (!string.IsNullOrEmpty(e.配偶者.氏名))
        {
            if (e.配偶者.障害 == 障害一般) 障一般++;
            else if (e.配偶者.障害 == 障害特別) { if (e.配偶者.同居) 障同居特別++; else 障特別++; }
        }

        for (int j = 0; j < 7; j++)
        {
            var f = e.扶養親族[j];
            if (string.IsNullOrEmpty(f.氏名)) continue;

            // 障害（16歳未満も含め障害者数には算入）
            if (f.障害 == 障害一般) 障一般++;
            else if (f.障害 == 障害特別) { if (f.同居) 障同居特別++; else 障特別++; }

            if (!Is16以上(f.生年月日, 判定年)) continue; // 16歳未満は控除対象扶養親族の数に入れない

            int 特定控除 = input.特定親族控除[j];
            if (Is19以上23未満(f.生年月日, 判定年) && 特定控除 == 0) 特定++;
            else if (特定控除 > 0) 特親++;
            else if (Is70(f.生年月日, 判定年)) { 老人人++; if (f.同居) 老人内++; }
            else 一般++; // 16-18・23-69 の一般扶養
        }

        Num("**扶養控除等の申告_一般の控除対象扶養親族", 一般);
        Num("**扶養控除等の申告_特定扶養親族", 特定);
        Num("**扶養控除等の申告_特定親族", 特親);
        Num("**扶養控除等の申告_老人扶養親族_同居老親等", 老人内);
        Num("**扶養控除等の申告_老人扶養親族_その他", 老人人 - 老人内);
        Num("**扶養控除等の申告_一般の障害者", 障一般);
        Num("**扶養控除等の申告_特別障害者", 障特別);
        Num("**扶養控除等の申告_同居特別障害者", 障同居特別);
    }

    // ── 和暦・年齢ヘルパー（源泉徴収票ビルダーと同一） ──
    private static readonly CultureInfo Ja = MakeJa();
    private static CultureInfo MakeJa()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("ja-JP").Clone();
        c.DateTimeFormat.Calendar = new JapaneseCalendar();
        return c;
    }
    private static string Wareki年(DateTime dt) => dt.ToString("yy", Ja).TrimStart('0');
    private static string Wareki元号(DateTime dt) => dt.ToString("gg", Ja);

    private static bool TryDate(string? s, out DateTime dt)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);

    private static bool 以上(string? ymd, int 判定年, int 年齢)
        => TryDate(ymd, out var dt) && DateOnly.FromDateTime(dt) <= new DateOnly(判定年 - 年齢, 1, 1);
    private static bool 未満(string? ymd, int 判定年, int 年齢)
        => TryDate(ymd, out var dt) && DateOnly.FromDateTime(dt) >= new DateOnly(判定年 - 年齢, 1, 2);
    private static bool Is70(string? ymd, int y) => 以上(ymd, y, 70);
    private static bool Is16以上(string? ymd, int y) => 以上(ymd, y, 16);
    private static bool Is19以上23未満(string? ymd, int y) => 以上(ymd, y, 19) && 未満(ymd, y, 23);
}
