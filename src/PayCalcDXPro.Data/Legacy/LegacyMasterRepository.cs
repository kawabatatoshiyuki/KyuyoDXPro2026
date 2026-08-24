using Microsoft.Data.Sqlite;
using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Data.Legacy;

/// <summary>
/// 復号済みの現行SQLiteから、給与計算に必要なマスタ（別表・標準報酬月額表・料率）を
/// ドメインモデルへ読み出す。データ移行・現行突合の中核。
/// </summary>
public sealed class LegacyMasterRepository
{
    private readonly SqliteConnection _conn;

    public LegacyMasterRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>源泉所得税（甲）の別表第一〜第四を読み出す。</summary>
    public WithholdingTaxTables LoadWithholdingTaxTables()
    {
        return new WithholdingTaxTables
        {
            給与所得控除 = ReadBrackets("T_月額表別表1", 率列: "控除率", 加算列: "控除額"),
            基礎控除 = ReadBrackets("T_月額表別表3", 率列: null, 加算列: "控除額"),
            税率速算 = ReadTaxRates("T_月額表別表4"),
            扶養控除単価 = ReadDecimal("select 配偶者控除 from T_月額表別表2 limit 1"),
        };
    }

    /// <summary>標準報酬月額表（健保・介護・厚年の被保険者負担分）と雇用保険料率を読み出す。</summary>
    public SocialInsuranceTable LoadSocialInsuranceTable()
    {
        var brackets = new List<SocialInsuranceBracket>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "select 報酬月額_自, 報酬月額_至, 健康保険, 厚生年金, 介護保険 from T_料率保険料額表 order by No";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                brackets.Add(new SocialInsuranceBracket
                {
                    // 報酬月額表は「至」が排他的上限（〇〇円未満）。
                    報酬月額下限 = D(r, 0) ?? 0m,
                    報酬月額上限 = D(r, 1) ?? decimal.MaxValue,
                    健康保険 = D(r, 2) ?? 0m,
                    厚生年金 = D(r, 3) ?? 0m,
                    介護保険 = D(r, 4) ?? 0m,
                });
            }
        }

        // 雇用保険一般（例:6）は 1000 分率。被保険者負担分。
        decimal 雇用一般 = ReadDecimal("select 雇用保険一般 from T_料率 limit 1");

        return new SocialInsuranceTable
        {
            区分 = brackets,
            雇用保険料率_本人負担 = 雇用一般 / 1000m,
        };
    }

    /// <summary>賞与の算出率表（甲）を読み出す。</summary>
    public BonusTaxRateTable LoadBonusTaxRateTable()
    {
        var rows = new List<BonusRateRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "select 金額0人,金額1人,金額2人,金額3人,金額4人,金額5人,金額6人,金額7人以上,賞与に乗ずる率 " +
            "from T_賞与源泉徴収税額甲 order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var th = new decimal[8];
            for (int i = 0; i < 8; i++) th[i] = D(r, i) ?? 0m;
            rows.Add(new BonusRateRow { 前月給与下限 = th, 賞与に乗ずる率 = D(r, 8) ?? 0m });
        }
        return new BonusTaxRateTable { 行 = rows };
    }

    /// <summary>賞与の算出率表（乙）を読み出す。現行 T_賞与源泉徴収税額乙。</summary>
    public BonusOtsuRateTable LoadBonusOtsuRateTable()
    {
        var rows = new List<BonusOtsuRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "select 金額, 賞与に乗ずる率 from T_賞与源泉徴収税額乙 order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new BonusOtsuRow
            {
                金額下限 = D(r, 0) ?? 0m,
                賞与に乗ずる率 = D(r, 1) ?? 0m,
            });
        }
        return new BonusOtsuRateTable { 行 = rows };
    }

    /// <summary>賞与の社会保険料計算用の被保険者負担率を読み出す。現行 T_料率。</summary>
    public BonusInsuranceRates LoadBonusInsuranceRates()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "select 健康保険被保険者負担分, 介護保険被保険者負担分, 厚生年金被保険者負担分, 雇用保険一般 " +
            "from T_料率 limit 1";
        using var r = cmd.ExecuteReader();
        r.Read();
        return new BonusInsuranceRates
        {
            健康保険率_本人 = D(r, 0) ?? 0m,
            介護保険率_本人 = D(r, 1) ?? 0m,
            厚生年金率_本人 = D(r, 2) ?? 0m,
            雇用保険率_本人 = D(r, 3) ?? 0m,
        };
    }

    /// <summary>年末調整の年額表（給与所得控除後・算出所得税速算）を読み出す。</summary>
    public IncomeTaxYearTables LoadYearEndTables()
    {
        var kyuyo = ReadBrackets2("T_給与所得控除後", "年調給与額_自", "年調給与額_至", "控除率", "控除額");

        var sokusan = new List<TaxRateRow>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "select 課税所得_自, 課税所得_至, 税率, 控除額 from T_課税給与所得金額 order by No";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                decimal 至 = D(r, 1) ?? decimal.MaxValue;
                sokusan.Add(new TaxRateRow
                {
                    下限 = D(r, 0) ?? 0m,
                    上限 = 至 == decimal.MaxValue ? decimal.MaxValue : 至 + 1m,
                    税率 = (D(r, 2) ?? 0m) / 100m,
                    控除額 = D(r, 3) ?? 0m,
                });
            }
        }

        return new IncomeTaxYearTables { 給与所得控除後 = kyuyo, 算出所得税速算 = sokusan };
    }

    /// <summary>年末調整の所得控除表（基礎控除・配偶者控除・生命保険料控除）を読み出す。</summary>
    public IncomeDeductionTables LoadIncomeDeductionTables()
    {
        // 基礎控除：控除後調整額_自/至 → 控除額（率0）
        var kiso = ReadBrackets2("T_基礎控除表", "控除後調整額_自", "控除後調整額_至", null, "控除額");

        // 配偶者控除
        var spouse = new List<SpouseDeductionRow>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText =
                "select 配偶者給与額_自, 配偶者給与額_至, 控除額１, 控除額２, 控除額３, 年齢ランク " +
                "from T_配偶者控除 order by No";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                decimal 至 = D(r, 1) ?? decimal.MaxValue;
                spouse.Add(new SpouseDeductionRow
                {
                    配偶者所得下限 = D(r, 0) ?? 0m,
                    配偶者所得上限 = 至 == decimal.MaxValue ? decimal.MaxValue : 至 + 1m,
                    控除額1 = D(r, 2) ?? 0m,
                    控除額2 = D(r, 3) ?? 0m,
                    控除額3 = D(r, 4) ?? 0m,
                    年齢ランク = r.IsDBNull(5) ? "0" : r.GetValue(5).ToString() ?? "0",
                });
            }
        }

        // 生命保険料控除（新＝T_生保控除１ / 旧＝T_生保控除２）
        var seihoNew = ReadBrackets2("T_生保控除１", "保険料額_自", "保険料額_至", "控除率", "控除額");
        var seihoOld = ReadBrackets2("T_生保控除２", "保険料額_自", "保険料額_至", "控除率", "控除額");

        return new IncomeDeductionTables
        {
            基礎控除 = kiso,
            配偶者控除 = spouse,
            生保控除_新 = seihoNew,
            生保控除_旧 = seihoOld,
        };
    }

    /// <summary>源泉所得税「乙欄」の各表（乙税第1〜3表・乙税階差）を読み出す。</summary>
    public OtsuTaxTables LoadOtsuTaxTables()
    {
        var 第1表 = ReadBrackets("T_乙税第１表", 率列: "控除率", 加算列: "控除額");
        var 第3表 = ReadTaxRatesNamed("T_乙税第３表", "給与額_自", "給与額_至", "控除率", "控除額");
        decimal 第2表 = ReadDecimal("select 控除額 from T_乙税第２表 limit 1");

        var 階差 = new List<乙税階差Row>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "select 給与額_自, 給与額_至, 階差 from T_乙税階差 order by No";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                decimal 至 = D(r, 1) ?? decimal.MaxValue;
                階差.Add(new 乙税階差Row
                {
                    給与額下限 = D(r, 0) ?? 0m,
                    給与額上限 = 至 == decimal.MaxValue ? decimal.MaxValue : 至 + 1m,
                    階差 = D(r, 2) ?? 1m,
                });
            }
        }

        return new OtsuTaxTables
        {
            乙税第1表 = 第1表,
            乙税第2表控除額 = 第2表,
            乙税第3表 = 第3表,
            乙税階差 = 階差,
        };
    }

    /// <summary>扶養・障害者等の控除額表（T_扶養障害者等、8区分）を読み出す。</summary>
    public DependentDeductionTable LoadDependentDeductionTable()
    {
        var amounts = new decimal[8];
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "select 区分, 控除額 from T_扶養障害者等 order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int kubun = (int)(D(r, 0) ?? 0m); // 区分 1〜8
            if (kubun >= 1 && kubun <= 8) amounts[kubun - 1] = D(r, 1) ?? 0m;
        }

        return new DependentDeductionTable
        {
            一般扶養 = amounts[0],      // 区分1
            同居特別障害 = amounts[1],  // 区分2
            特別障害 = amounts[2],      // 区分3
            一般障害 = amounts[3],      // 区分4
            ひとり親 = amounts[4],      // 区分5
            同居老親加算 = amounts[5],  // 区分6
            特定扶養加算 = amounts[6],  // 区分7
            老人その他加算 = amounts[7],// 区分8
        };
    }

    /// <summary>特定親族特別控除の控除額表（T_特定親族特別控除）を読み出す。現行に該当テーブルが無い場合は呼出側で要注意。</summary>
    public SpecialRelativeDeductionTable LoadSpecialRelativeDeductionTable()
    {
        var rows = new List<SpecialRelativeDeductionRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "select 合計所得金額_自, 合計所得金額_至, 収入金額_自, 収入金額_至, 特定親族特別控除額 " +
            "from T_特定親族特別控除 order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new SpecialRelativeDeductionRow
            {
                // 区間は現行SQL（自 < 金額 AND 至 ≧ 金額）に合わせ「下限は排他・上限は包含」。
                合計所得下限 = D(r, 0) ?? 0m,
                合計所得上限 = D(r, 1) ?? decimal.MaxValue,
                収入下限 = D(r, 2) ?? 0m,
                収入上限 = D(r, 3) ?? decimal.MaxValue,
                控除額 = D(r, 4) ?? 0m,
            });
        }
        return new SpecialRelativeDeductionTable { 行 = rows };
    }

    /// <summary>公的年金等の速算表（T_速算表）を読み出す。</summary>
    public PensionConversionTable LoadPensionConversionTable()
    {
        var rows = new List<PensionConversionRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "select 控除後ランク区分, 年齢ランク, 配偶者支給額_自, 配偶者支給額_至, 控除率, 控除額 " +
            "from T_速算表 order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new PensionConversionRow
            {
                控除後ランク = r.IsDBNull(0) ? "" : r.GetValue(0).ToString() ?? "",
                年齢ランク = r.IsDBNull(1) ? "" : r.GetValue(1).ToString() ?? "",
                支給額下限 = D(r, 2) ?? 0m,
                支給額上限 = D(r, 3) ?? decimal.MaxValue,
                控除率 = D(r, 4) ?? 0m,
                控除額 = D(r, 5) ?? 0m,
            });
        }
        return new PensionConversionTable { 行 = rows };
    }

    // ---- helpers ----

    private IReadOnlyList<BracketRow> ReadBrackets2(string table, string 自, string 至col, string? 率col, string 加算col)
    {
        var rows = new List<BracketRow>();
        using var cmd = _conn.CreateCommand();
        string 率sel = 率col is null ? "null as 率" : $"\"{率col}\" as 率";
        cmd.CommandText = $"select \"{自}\", \"{至col}\", {率sel}, \"{加算col}\" from \"{table}\" order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            decimal 至 = D(r, 1) ?? decimal.MaxValue;
            rows.Add(new BracketRow
            {
                下限 = D(r, 0) ?? 0m,
                上限 = 至 == decimal.MaxValue ? decimal.MaxValue : 至 + 1m,
                率 = (D(r, 2) ?? 0m) / 100m,
                加算額 = D(r, 3) ?? 0m,
            });
        }
        return rows;
    }

    private IReadOnlyList<BracketRow> ReadBrackets(string table, string? 率列, string 加算列)
    {
        var rows = new List<BracketRow>();
        using var cmd = _conn.CreateCommand();
        string 率sel = 率列 is null ? "null as 率" : $"{率列} as 率";
        cmd.CommandText = $"select 給与額_自, 給与額_至, {率sel}, {加算列} as 加算 from \"{table}\" order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            decimal 至 = D(r, 1) ?? decimal.MaxValue;
            rows.Add(new BracketRow
            {
                下限 = D(r, 0) ?? 0m,
                // 税系の別表は「至」が包含的上限のため +1 して排他的上限へ変換。
                上限 = 至 == decimal.MaxValue ? decimal.MaxValue : 至 + 1m,
                率 = (D(r, 2) ?? 0m) / 100m,
                加算額 = D(r, 3) ?? 0m,
            });
        }
        return rows;
    }

    private IReadOnlyList<TaxRateRow> ReadTaxRates(string table)
    {
        var rows = new List<TaxRateRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"select 給与額_自, 給与額_至, 税率, 控除額 from \"{table}\" order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            decimal 至 = D(r, 1) ?? decimal.MaxValue;
            rows.Add(new TaxRateRow
            {
                下限 = D(r, 0) ?? 0m,
                上限 = 至 == decimal.MaxValue ? decimal.MaxValue : 至 + 1m,
                税率 = (D(r, 2) ?? 0m) / 100m,
                控除額 = D(r, 3) ?? 0m,
            });
        }
        return rows;
    }

    private IReadOnlyList<TaxRateRow> ReadTaxRatesNamed(string table, string 自, string 至col, string 税率col, string 控除col)
    {
        var rows = new List<TaxRateRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"select \"{自}\", \"{至col}\", \"{税率col}\", \"{控除col}\" from \"{table}\" order by No";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            decimal 至 = D(r, 1) ?? decimal.MaxValue;
            rows.Add(new TaxRateRow
            {
                下限 = D(r, 0) ?? 0m,
                上限 = 至 == decimal.MaxValue ? decimal.MaxValue : 至 + 1m,
                税率 = (D(r, 2) ?? 0m) / 100m,
                控除額 = D(r, 3) ?? 0m,
            });
        }
        return rows;
    }

    private decimal ReadDecimal(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        object? v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? 0m : Convert.ToDecimal(v);
    }

    private static decimal? D(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        string s = r.GetValue(i)?.ToString() ?? "";
        return decimal.TryParse(s, out decimal d) ? d : null;
    }
}
