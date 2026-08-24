using System.Globalization;
using PayCalcDXPro.Reporting;

namespace PayCalcDXPro.Web.Summary;

/// <summary>
/// 年末調整 集計表（納付書用）の集計値と明細を組み立てる（現行 R_集計表.createReport の移植）。
/// レンダリングは <see cref="SummaryReport"/> が担い、本クラスは業務ロジックに徹する。
///
/// 重要な符号規約の差異：
///  ・ブラウザ版 過不足額 = 源泉計 − 年調年税額（＋＝過納〔超過・還付〕、−＝不足〔追加徴収〕）。
///  ・現行 R_集計表 は「過不足額 &gt; 0 → 不足税額」＝逆符号（現行 過不足額 = 年調年税額 − 源泉計）。
///  ・よって本ビルダーは「調整額（納付書ベース）＝ −過不足額」に反転してから現行ロジックを適用する。
///    調整額 &gt; 0 = 不足、調整額 &lt; 0 = 超過。明細の調整額欄も納付書ベース（不足＝正／超過＝負）で表示する。
/// </summary>
public static class SummaryBuilder
{
    /// <summary>集計表の全入力（対象従業員・集計期間・賞与範囲・支給年）。</summary>
    public sealed record Input(
        int 支給年,
        int SyukeiCheck,   // 1=納期特例(7〜12月)、2=一般(12月のみ)
        int SyouyoCheck,   // 1=4回目のみ,2=3・4,3=2〜4,4=1〜4,5=含まない
        IReadOnlyList<Employee> 従業員);

    /// <summary>集計対象の従業員1名分（年末調整の計算済み値）。配列は暦月/回インデックス（[6]=7月, [11]=12月）。</summary>
    public sealed record Employee(
        string No, string 氏名, string 乙欄, int 過不足額,
        int[] 給与, int[] 源泉, int[] 賞与, int[] 源泉賞与);

    /// <summary>組み立て結果（集計値の marker→表示文字列 と 明細一覧）。</summary>
    public sealed record Result(
        IReadOnlyDictionary<string, string?> 集計値,
        IReadOnlyList<SummaryReport.Detail> 明細);

    public static Result Build(Input input)
    {
        int kyuyoNinzu = 0, kyuyoKei = 0, gensenKei = 0;
        int syouyoNinzu = 0, syouyoKei = 0, gensenSyouyoKei = 0;
        int husoku = 0, tyouka = 0;   // 不足税額計（正）／超過税額計（負）

        var details = new List<SummaryReport.Detail>(input.従業員.Count);

        foreach (Employee e in input.従業員)
        {
            // ── 俸給・給与等（集計期間で分岐）──
            if (input.SyukeiCheck == 1)              // 納期特例：7〜12月
            {
                for (int m = 6; m <= 11; m++)        // idx6=7月 … idx11=12月
                {
                    if (e.給与[m] != 0) kyuyoNinzu++;
                    kyuyoKei += e.給与[m];
                    gensenKei += e.源泉[m];
                }
            }
            else if (input.SyukeiCheck == 2)         // 一般：12月のみ
            {
                if (e.給与[11] != 0) kyuyoNinzu++;
                kyuyoKei += e.給与[11];
                gensenKei += e.源泉[11];
            }

            // ── 賞与（役員報酬を除く）：期間に含む賞与で分岐 ──
            //   1=4回目のみ / 2=3,4 / 3=2,3,4 / 4=1,2,3,4 / 5=含まない
            // 賞与開始インデックス（5=含まないは開始4で反復なし）。
            int 賞与開始 = input.SyouyoCheck switch { 1 => 3, 2 => 2, 3 => 1, 4 => 0, _ => 4 };
            for (int b = 賞与開始; b <= 3; b++)
            {
                if (e.賞与[b] != 0) syouyoNinzu++;
                syouyoKei += e.賞与[b];
                gensenSyouyoKei += e.源泉賞与[b];
            }

            // ── 明細（整理番号・氏名・調整額）──
            //   調整額（納付書ベース）＝ −過不足額。年末調整した（乙欄=="1"）者のみ調整額を表示。
            int 調整額 = -e.過不足額;
            string? 調整額表示 = (e.乙欄 == "1" && 調整額 != 0)
                ? 調整額.ToString("#,##0", CultureInfo.InvariantCulture)
                : null;
            details.Add(new SummaryReport.Detail(e.No, e.氏名, 調整額表示));

            // ── 不足税額計／超過税額計 ──
            if (調整額 > 0) husoku += 調整額;
            else tyouka += 調整額;
        }

        int zeiKei = gensenKei + gensenSyouyoKei + husoku + tyouka;

        string Money(int v) => v.ToString("#,##0", CultureInfo.InvariantCulture);
        string 年度 = $"{Wareki.元号(input.支給年)}{Wareki.年数(input.支給年)}年 年末調整　集計表（納付書用）";

        var 集計 = new Dictionary<string, string?>
        {
            ["**年度"] = 年度,
            ["**種別"] = input.SyukeiCheck == 1 ? "*特例納付" : null,
            ["**俸給・給与等_人員"] = kyuyoNinzu.ToString(CultureInfo.InvariantCulture),
            ["**俸給・給与等_支給額"] = Money(kyuyoKei),
            ["**俸給・給与等_税額"] = Money(gensenKei),
            ["**賞与(役員報酬を除く）_人員"] = syouyoNinzu.ToString(CultureInfo.InvariantCulture),
            ["**賞与(役員報酬を除く）_支給額"] = Money(syouyoKei),
            ["**賞与(役員報酬を除く）_税額"] = Money(gensenSyouyoKei),
            ["**年末調整による不足税額_税額"] = Money(husoku),
            ["**年末調整による超過税額_税額"] = Money(tyouka),
            ["**本税_税額"] = zeiKei == 0 ? null : Money(zeiKei),
        };

        return new Result(集計, details);
    }
}
