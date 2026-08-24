using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Company;
using PayCalcDXPro.Data.YearEnd;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Reporting;
using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Web.LegalRecord;

/// <summary>
/// 給与所得の源泉徴収票等の法定調書合計表（現行 R_合計表）の集計入力を、
/// DB各テーブル（T_年末調整/T_退職源泉情報/T_年調報酬情報/T_不動産情報/T_不動産譲受/T_不動産あっせん情報）と
/// 会社情報・支給年から組み立てる。集計そのものは Domain の <see cref="Domain.Services.LegalRecordSummaryCalculator"/>
/// が担い、本クラスは DB→Domain行 のマッピングに徹する（現行 createReport のデータ取得部を移植）。
///
/// 各支払調書は現行 SQL の抽出条件「名称/氏名が空でない」に合わせ、空名称レコードを除外する。
/// 不動産系の「提出が必要」は現行どおり列「不要」の値=="1"（＝repoの bool 不要=true）で判定する。
/// </summary>
public static class LegalRecordSummaryBuilder
{
    private static decimal D(int? v) => v ?? 0;

    /// <summary>年末調整の計算済みエントリ1件 → 給与支払行（給与所得の源泉徴収票合計表の1人分）。</summary>
    public static 給与支払行 給与行(YearEndEntry e) => new()
    {
        給与賞与計 = e.給与賞与計,
        前社収入 = e.前社収入,
        年調 = e.乙欄 == "1",         // 乙欄=="1"（年末調整する）→年調年税額、それ以外→源泉計
        年調年税額 = e.年調年税額,
        源泉計 = e.源泉計,
        前社源泉 = e.前社源泉,
        源泉徴収票提出 = e.源泉徴収票提出,
    };

    /// <summary>2. 退職所得の源泉徴収票（T_退職源泉情報）。</summary>
    public static IReadOnlyList<退職支払行> 退職(SqliteConnection conn)
    {
        var repo = new RetirementWithholdingRepository(conn);
        var rows = new List<退職支払行>();
        foreach (var item in repo.List())
        {
            if (string.IsNullOrEmpty(item.氏名)) continue;
            var r = repo.Load(item.No);
            if (r is null) continue;
            rows.Add(new 退職支払行
            {
                提出 = r.提出,
                支払金額1 = r.支払金額[0], 支払金額2 = r.支払金額[1], 支払金額3 = r.支払金額[2],
                源泉徴収税額1 = r.源泉徴収税額[0], 源泉徴収税額2 = r.源泉徴収税額[1], 源泉徴収税額3 = r.源泉徴収税額[2],
            });
        }
        return rows;
    }

    /// <summary>3. 報酬、料金、契約金及び賞金の支払調書（T_年調報酬情報）。号=区分1(1〜8)。</summary>
    public static IReadOnlyList<報酬支払行> 報酬(SqliteConnection conn)
    {
        var repo = new RewardRepository(conn);
        var rows = new List<報酬支払行>();
        foreach (var item in repo.List())
        {
            if (string.IsNullOrEmpty(item.名称)) continue;
            var r = repo.Load(item.No);
            if (r is null) continue;
            int.TryParse(r.区分[0], out int 号);
            rows.Add(new 報酬支払行
            {
                提出 = r.提出,
                受者区分 = r.支払受者区分 == "2" ? 支払受者区分.個人 : 支払受者区分.法人,
                号 = 号,
                支払金額 = r.支払金額.Select(D).ToArray(),
                源泉徴収税額 = r.源泉徴収税額.Select(D).ToArray(),
            });
        }
        return rows;
    }

    /// <summary>4. 不動産の使用料等の支払調書（T_不動産情報）。</summary>
    public static IReadOnlyList<不動産支払行> 不動産(SqliteConnection conn)
    {
        var repo = new RealEstateRepository(conn);
        var rows = new List<不動産支払行>();
        foreach (var item in repo.List())
        {
            if (string.IsNullOrEmpty(item.名称)) continue;
            var r = repo.Load(item.No);
            if (r is null) continue;
            rows.Add(new 不動産支払行 { 提出要 = r.不要, 支払金額 = r.支払金額.Select(D).ToArray() });
        }
        return rows;
    }

    /// <summary>5. 不動産等の譲受けの対価の支払調書（T_不動産譲受）。</summary>
    public static IReadOnlyList<不動産支払行> 譲受(SqliteConnection conn)
    {
        var repo = new RealEstateTransferRepository(conn);
        var rows = new List<不動産支払行>();
        foreach (var item in repo.List())
        {
            if (string.IsNullOrEmpty(item.名称)) continue;
            var r = repo.Load(item.No);
            if (r is null) continue;
            rows.Add(new 不動産支払行 { 提出要 = r.不要, 支払金額 = r.支払金額.Select(D).ToArray() });
        }
        return rows;
    }

    /// <summary>6. 不動産等の売買又は貸付けのあっせん手数料の支払調書（T_不動産あっせん情報）。</summary>
    public static IReadOnlyList<不動産支払行> あっせん(SqliteConnection conn)
    {
        var repo = new RealEstateMediationRepository(conn);
        var rows = new List<不動産支払行>();
        foreach (var item in repo.List())
        {
            if (string.IsNullOrEmpty(item.名称)) continue;
            var r = repo.Load(item.No);
            if (r is null) continue;
            rows.Add(new 不動産支払行 { 提出要 = r.不要, 支払金額 = r.支払金額.Select(D).ToArray() });
        }
        return rows;
    }

    /// <summary>提出者欄（M_会社情報＋支給年）。年は和暦年数のみ（元号は様式に固定）。</summary>
    public static 法定調書提出者情報 提出者(CompanyInfo c, int 支給年) => new()
    {
        年 = 支給年 > 0 ? Wareki.年数(支給年) : null,
        署番号 = c.署番号,
        税務署名 = c.税務署名,
        事業種目 = c.事業種目,
        整理番号 = c.整理番号,
        所在地 = c.所在地,
        電話番号 = c.ＴＥＬ,
        会社名 = c.会社名,
        会社名カナ = c.会社名カナ,
        代表者名 = c.代表者名,
        代表者カナ = c.代表者氏名カナ,
        事業所番号 = c.事業所マイナンバー,
        番号区分 = c.番号区分,
    };
}
