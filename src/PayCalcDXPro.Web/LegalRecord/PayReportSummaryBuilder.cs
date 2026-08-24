using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Company;
using PayCalcDXPro.Reporting;

namespace PayCalcDXPro.Web.LegalRecord;

/// <summary>
/// 給与支払報告書（総括表）（現行 R_総括表）の描画値を、DB各テーブルから組み立てる。
/// マーカー→表示文字列の辞書を返し、<see cref="PayReportSummaryReport"/> が背景様式へ重ねる
/// （現行 createReport のセル代入を忠実に移植）。
///
/// 提出先（M_報告書提出先情報）ごとに、住民税コード＝提出先番号で T_年末調整 を結合して
/// 支払期間（給与N月>0 の最小/最大月）・報告人数・退職者人員を集計する（現行 strHks を移植）。
/// 提出先番号='1'（特別徴収対象外）・市区町村名 未設定は対象外。最大4件を各象限（_1〜_4）に割り当てる。
/// </summary>
public static class PayReportSummaryBuilder
{
    // 現行 MConst 実測値。
    private const string CHECK_BOX_ON = "1"; // T_年末調整.退職 のチェックオン
    private const string OTSU_3 = "3";       // 乙欄＝乙（年末調整対象外だが報告対象）

    /// <summary>提出先1件分の集計（1象限に対応）。</summary>
    public sealed record 提出先集計(string 市町村名, int? 月分から, int? 月分まで, int 報告人数, int 退職人数);

    /// <summary>提出先ごとの集計を提出先番号順に返す（現行 strHks）。市区町村名未設定・番号'1'は除外。</summary>
    public static IReadOnlyList<提出先集計> 集計(SqliteConnection conn)
    {
        const string sql = """
            select
                S1.市町村名,
                min(S1.月分から) as 月分から,
                max(S1.月分まで) as 月分まで,
                sum(S1.退職対象)  as 退職人数,
                sum(S1.報告対象)  as 総人数
            from (
                select
                    M1.市区町村名 as 市町村名,
                    M1.提出先番号 as 提出先番号,
                    case
                        when T1.給与1月  > 0 then 1  when T1.給与2月  > 0 then 2
                        when T1.給与3月  > 0 then 3  when T1.給与4月  > 0 then 4
                        when T1.給与5月  > 0 then 5  when T1.給与6月  > 0 then 6
                        when T1.給与7月  > 0 then 7  when T1.給与8月  > 0 then 8
                        when T1.給与9月  > 0 then 9  when T1.給与10月 > 0 then 10
                        when T1.給与11月 > 0 then 11 when T1.給与12月 > 0 then 12
                        else null
                    end as 月分から,
                    case
                        when T1.給与12月 > 0 then 12 when T1.給与11月 > 0 then 11
                        when T1.給与10月 > 0 then 10 when T1.給与9月  > 0 then 9
                        when T1.給与8月  > 0 then 8  when T1.給与7月  > 0 then 7
                        when T1.給与6月  > 0 then 6  when T1.給与5月  > 0 then 5
                        when T1.給与4月  > 0 then 4  when T1.給与3月  > 0 then 3
                        when T1.給与2月  > 0 then 2  when T1.給与1月  > 0 then 1
                        else null
                    end as 月分まで,
                    case when T1.退職 = $on then 1 else 0 end as 退職対象,
                    case when T1.住民税コード is not null then 1 else 0 end as 報告対象
                from M_報告書提出先情報 M1
                left join T_年末調整 T1
                    on M1.提出先番号 = T1.住民税コード
                    and (
                        (
                            (T1.乙欄 <> $otsu or T1.乙欄 is null)
                            and (T1.退職 <> $on or T1.退職 is null)
                            and T1.給与賞与計 > 0
                        )
                        or (T1.乙欄 = $otsu)
                        or (T1.退職 = $on)
                    )
                where M1.提出先番号 <> '1' and M1.市区町村名 is not null
            ) S1
            group by S1.市町村名, S1.提出先番号
            order by S1.提出先番号 + 0 asc
            """;

        var rows = new List<提出先集計>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$on", CHECK_BOX_ON);
        cmd.Parameters.AddWithValue("$otsu", OTSU_3);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new 提出先集計(
                市町村名: r.IsDBNull(0) ? "" : r.GetString(0),
                月分から: r.IsDBNull(1) ? null : r.GetInt32(1),
                月分まで: r.IsDBNull(2) ? null : r.GetInt32(2),
                退職人数: r.IsDBNull(3) ? 0 : r.GetInt32(3),
                報告人数: r.IsDBNull(4) ? 0 : r.GetInt32(4)));
        }
        return rows;
    }

    /// <summary>受給者総人数（T_従業員情報の件数）。現行 strCnt。</summary>
    public static int 受給者総人数(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(1) from T_従業員情報";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// 会社情報・支給年・提出先集計・受給者総人数から、総括表のマーカー→表示文字列の辞書を組み立てる。
    /// 会社（提出者）情報は全4象限に同じ値が重なる（ExtractAll による重複マーカーへ同一値を供給）。
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Build(
        CompanyInfo c, int 支給年, IReadOnlyList<提出先集計> 提出先, int 受給者総人数)
    {
        var d = new Dictionary<string, string?>();

        // 給与の支払期間（和暦年数）。現行は adToJapCal(FORMAT_1)。
        if (支給年 > 0) d["**給与の支払期間_年"] = Wareki.年数(支給年);

        // 給与支払者の個人番号又は法人番号（13桁マス。_13桁=左端 … _1桁=右端）。
        Set個人番号(d, c.事業所マイナンバー ?? "", c.番号区分 ?? "1");

        d["**給与支払者の氏名又は名称（フリガナ）"] = c.会社名カナ;
        d["**給与支払者の氏名又は名称"] = c.会社名;
        d["**所得税の源泉徴収をしている事務所又は事業の名称"] = c.事業所名;
        d["**同上の所在地（フリガナ）"] = c.所在地カナ;
        d["**同上の所在地"] = c.所在地;
        d["**給与支払者が法人である場合の代表者の氏名"] = c.代表者名;
        d["**連絡者の氏名、所属課、係名及び電話番号_連絡者名"] = c.連絡者名;
        d["**連絡者の氏名、所属課、係名及び電話番号_電話番号"] = c.ＴＥＬ;
        d["**事業種目"] = c.事業種目;
        d["**受給者総人数"] = 受給者総人数.ToString();
        d["**所属税務署"] = c.税務署名;
        d["**給与の支払方法及びその期日"] = c.支払方法;
        d["**特別徴収税額の払込みを希望する金融機関_（名称）"] = c.支払金融機関;
        d["**特別徴収税額の払込みを希望する金融機関_（所在地）"] = c.金融機関所在地;

        // 提出先を4象限（_1〜_4）へ割り当てる。実データが無い象限は現行どおり「長殿」・0人。
        for (int i = 1; i <= 4; i++)
        {
            提出先集計? row = i <= 提出先.Count ? 提出先[i - 1] : null;
            d[$"**市町村名_{i}"] = (row?.市町村名 ?? "") + "長殿";
            d[$"**給与の支払期間_月分から_{i}"] = row?.月分から?.ToString() ?? "";
            d[$"**給与の支払期間_月分まで_{i}"] = row?.月分まで?.ToString() ?? "";
            d[$"**報告人数_{i}"] = (row?.報告人数 ?? 0).ToString();
            d[$"**報告人員のうち退職者人員_{i}"] = (row?.退職人数 ?? 0).ToString();
        }

        return d;
    }

    /// <summary>
    /// 個人番号又は法人番号を13桁マス（_13桁=左端 … _1桁=右端）へ割り当てる。
    /// 現行 R_総括表：法人(番号区分=1)は13桁を _13桁〜_1桁、個人(それ以外)は12桁を _13桁〜_2桁（_1桁は空欄）。
    /// 桁数が区分と一致しない場合は現行同様なにも設定しない（空欄）。
    /// </summary>
    private static void Set個人番号(Dictionary<string, string?> d, string number, string 番号区分)
    {
        int expected = 番号区分 == "1" ? 13 : 12;
        if (string.IsNullOrEmpty(number) || number.Length != expected) return;

        // マス K（1..13、_13桁=左端）の文字＝number[13-K]。個人12桁では K=1（idx12）が範囲外＝空欄。
        for (int k = 1; k <= 13; k++)
        {
            int idx = 13 - k;
            if (idx >= 0 && idx < number.Length)
                d[$"**給与支払者の個人番号又は法人番号_{k}桁"] = number[idx].ToString();
        }
    }
}
