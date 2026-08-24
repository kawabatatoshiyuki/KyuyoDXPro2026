using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 年末調整（T_年末調整）の読み書き。現行 F_年調_従業員情報 のデータ操作を移植。
/// T_年末調整 は現行スキーマと互換の全列を持つ（EnsureSchema は現行DDLに一致）。
/// MVPでは「計算が完結する最小構成」の列のみ Load/Save し、扶養親族明細・前職などの列は保持（変更しない）。
/// 年間集計（給与N月/賞与N/社保/源泉/各種「計」）は給与・賞与トランから Aggregate で再計算する。
/// </summary>
public sealed class YearEndRepository
{
    private readonly SqliteConnection _conn;

    public YearEndRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>T_年末調整 を用意する（現行スキーマと同一。既存DBがあればそのまま使う）。</summary>
    public void EnsureSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_年末調整" (
              "Number"  "TEXT",
              "乙欄"  "TEXT",
              "源泉徴収票提出"  "TEXT",
              "源泉徴収票摘要欄追記文"  "TEXT",
              "住民税コード"  "TEXT",
              "寡婦"  "TEXT",
              "障害"  "TEXT",
              "未成年"  "TEXT",
              "勤労学生"  "TEXT",
              "死亡退職"  "TEXT",
              "災害者"  "TEXT",
              "外国人"  "TEXT",
              "所得金額調整控除適用区分"  "TEXT",
              "所得金額調整控除適用の扶養者氏名"  "TEXT",
              "就職"  "TEXT",
              "中途就職日"  "TEXT",
              "退職"  "TEXT",
              "中途退職日"  "TEXT",
              "前社の名称"  "TEXT",
              "所在地"  "TEXT",
              "前社収入"  "INTEGER",
              "前社社保"  "INTEGER",
              "前社源泉"  "INTEGER",
              "前社退職日"  "TEXT",
              "新生命保険"  "INTEGER",
              "生命保険"  "INTEGER",
              "介護医療保険"  "INTEGER",
              "新個人年金"  "INTEGER",
              "個人年金"  "INTEGER",
              "申告社保"  "INTEGER",
              "国民年金保険料等"  "INTEGER",
              "地震保険"  "INTEGER",
              "長期損害"  "INTEGER",
              "小規模共済"  "INTEGER",
              "住宅借入金特別控除可能額"  "INTEGER",
              "新築又は購入に係る借入金"  "INTEGER",
              "新築又は購入居住開始年月日"  "TEXT",
              "新築又は購入適用区分"  "TEXT",
              "増改築等に係る借入金"  "INTEGER",
              "増改築等居住開始年月日"  "TEXT",
              "増改築等適用区分"  "TEXT",
              "配偶者"  "TEXT",
              "配偶者フリガナ"  "TEXT",
              "配偶者生年月日"  "TEXT",
              "配偶者収入種類"  "TEXT",
              "配偶者収入"  "TEXT",
              "配偶者障害"  "TEXT",
              "同居配偶者"  "TEXT",
              "配偶者非居住者"  "TEXT",
              "扶養親族名前1"  "TEXT", "扶養親族1フリガナ"  "TEXT", "扶養親族生年月日1"  "TEXT", "扶養親族続柄1"  "TEXT", "扶養親族収入種類1"  "TEXT", "扶養親族収入1"  "INTEGER", "扶養親族障害1"  "TEXT", "扶養親族同居1"  "TEXT", "扶養親族1非居住者"  "TEXT",
              "扶養親族名前2"  "TEXT", "扶養親族2フリガナ"  "TEXT", "扶養親族生年月日2"  "TEXT", "扶養親族続柄2"  "TEXT", "扶養親族収入種類2"  "TEXT", "扶養親族収入2"  "INTEGER", "扶養親族障害2"  "TEXT", "扶養親族同居2"  "TEXT", "扶養親族2非居住者"  "TEXT",
              "扶養親族名前3"  "TEXT", "扶養親族3フリガナ"  "TEXT", "扶養親族生年月日3"  "TEXT", "扶養親族続柄3"  "TEXT", "扶養親族収入種類3"  "TEXT", "扶養親族収入3"  "INTEGER", "扶養親族障害3"  "TEXT", "扶養親族同居3"  "TEXT", "扶養親族3非居住者"  "TEXT",
              "扶養親族名前4"  "TEXT", "扶養親族4フリガナ"  "TEXT", "扶養親族生年月日4"  "TEXT", "扶養親族続柄4"  "TEXT", "扶養親族収入種類4"  "TEXT", "扶養親族収入4"  "INTEGER", "扶養親族障害4"  "TEXT", "扶養親族同居4"  "TEXT", "扶養親族4非居住者"  "TEXT",
              "扶養親族名前5"  "TEXT", "扶養親族5フリガナ"  "TEXT", "扶養親族生年月日5"  "TEXT", "扶養親族続柄5"  "TEXT", "扶養親族収入種類5"  "TEXT", "扶養親族収入5"  "INTEGER", "扶養親族障害5"  "TEXT", "扶養親族同居5"  "TEXT", "扶養親族5非居住者"  "TEXT",
              "扶養親族名前6"  "TEXT", "扶養親族6フリガナ"  "TEXT", "扶養親族生年月日6"  "TEXT", "扶養親族続柄6"  "TEXT", "扶養親族収入種類6"  "TEXT", "扶養親族収入6"  "INTEGER", "扶養親族障害6"  "TEXT", "扶養親族同居6"  "TEXT", "扶養親族6非居住者"  "TEXT",
              "扶養親族名前7"  "TEXT", "扶養親族7フリガナ"  "TEXT", "扶養親族生年月日7"  "TEXT", "扶養親族続柄7"  "TEXT", "扶養親族収入種類7"  "TEXT", "扶養親族収入7"  "INTEGER", "扶養親族障害7"  "TEXT", "扶養親族同居7"  "TEXT", "扶養親族7非居住者"  "TEXT",
              "給与1月"  "INTEGER", "給与2月"  "INTEGER", "給与3月"  "INTEGER", "給与4月"  "INTEGER", "給与5月"  "INTEGER", "給与6月"  "INTEGER", "給与7月"  "INTEGER", "給与8月"  "INTEGER", "給与9月"  "INTEGER", "給与10月"  "INTEGER", "給与11月"  "INTEGER", "給与12月"  "INTEGER",
              "賞与1"  "INTEGER", "賞与2"  "INTEGER", "賞与3"  "INTEGER", "賞与4"  "INTEGER",
              "社保1月"  "INTEGER", "社保2月"  "INTEGER", "社保3月"  "INTEGER", "社保4月"  "INTEGER", "社保5月"  "INTEGER", "社保6月"  "INTEGER", "社保7月"  "INTEGER", "社保8月"  "INTEGER", "社保9月"  "INTEGER", "社保10月"  "INTEGER", "社保11月"  "INTEGER", "社保12月"  "INTEGER",
              "社保賞与1"  "INTEGER", "社保賞与2"  "INTEGER", "社保賞与3"  "INTEGER", "社保賞与4"  "INTEGER",
              "源泉1月"  "INTEGER", "源泉2月"  "INTEGER", "源泉3月"  "INTEGER", "源泉4月"  "INTEGER", "源泉5月"  "INTEGER", "源泉6月"  "INTEGER", "源泉7月"  "INTEGER", "源泉8月"  "INTEGER", "源泉9月"  "INTEGER", "源泉10月"  "INTEGER", "源泉11月"  "INTEGER", "源泉12月"  "INTEGER",
              "源泉賞与1"  "INTEGER", "源泉賞与2"  "INTEGER", "源泉賞与3"  "INTEGER", "源泉賞与4"  "INTEGER",
              "住宅控除"  "INTEGER",
              "給与計"  "INTEGER", "給与社保計"  "INTEGER", "給与源泉計"  "INTEGER",
              "賞与計"  "INTEGER", "賞与社保計"  "INTEGER", "賞与源泉計"  "INTEGER",
              "給与賞与計"  "INTEGER", "源泉計"  "INTEGER",
              "給与所得控除後"  "INTEGER", "給与所得控除後調整控除後"  "INTEGER", "給与社保"  "INTEGER",
              "生保控除"  "INTEGER", "地震控除"  "INTEGER",
              "配偶者所得"  "INTEGER", "配偶者特別控除"  "INTEGER",
              "特定親族1控除"  "INTEGER", "特定親族2控除"  "INTEGER", "特定親族3控除"  "INTEGER", "特定親族4控除"  "INTEGER", "特定親族5控除"  "INTEGER", "特定親族6控除"  "INTEGER", "特定親族7控除"  "INTEGER", "特定親族特別控除額"  "INTEGER",
              "基礎控除"  "INTEGER", "障害配偶者控除"  "INTEGER",
              "扶養1控除"  "INTEGER", "扶養2控除"  "INTEGER", "扶養3控除"  "INTEGER", "扶養4控除"  "INTEGER", "扶養5控除"  "INTEGER", "扶養6控除"  "INTEGER", "扶養7控除"  "INTEGER",
              "扶養控除等合計"  "INTEGER", "所得控除計"  "INTEGER", "課税所得"  "INTEGER", "所得税"  "INTEGER", "年調年税額"  "INTEGER", "過不足額"  "INTEGER",
              "給与扶養親族数1月"  "INTEGER", "給与扶養親族数2月"  "INTEGER", "給与扶養親族数3月"  "INTEGER", "給与扶養親族数4月"  "INTEGER", "給与扶養親族数5月"  "INTEGER", "給与扶養親族数6月"  "INTEGER", "給与扶養親族数7月"  "INTEGER", "給与扶養親族数8月"  "INTEGER", "給与扶養親族数9月"  "INTEGER", "給与扶養親族数10月"  "INTEGER", "給与扶養親族数11月"  "INTEGER", "給与扶養親族数12月"  "INTEGER",
              "賞与扶養親族数1回目"  "INTEGER", "賞与扶養親族数2回目"  "INTEGER", "賞与扶養親族数3回目"  "INTEGER", "賞与扶養親族数4回目"  "INTEGER",
              "年調所得税額"  "INTEGER", "金額"  "INTEGER", "算出根拠"  "INTEGER", "所得者本人控除"  "INTEGER", "所得金額調整控除額"  "INTEGER",
              "扶養親族留学送金区分1"  "TEXT", "扶養親族留学送金区分2"  "TEXT", "扶養親族留学送金区分3"  "TEXT", "扶養親族留学送金区分4"  "TEXT", "扶養親族留学送金区分5"  "TEXT", "扶養親族留学送金区分6"  "TEXT", "扶養親族留学送金区分7"  "TEXT",
              PRIMARY KEY("Number")
            );
            """);
    }

    /// <summary>年末調整の対象者一覧（在籍者を先・退職者を後ろ、各グループ内は並び順）。</summary>
    public IReadOnlyList<YearEndTarget> Targets()
    {
        var list = new List<YearEndTarget>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT e.Number, e.No, e.氏名, IFNULL(s.値,'') AS 就退区分名, e.就退区分
            FROM T_従業員情報 e
            LEFT JOIN M_コード s ON s.項目区分 = '014' AND s.コード = e.就退区分
            ORDER BY CASE WHEN e.就退区分 IN ('1','2') THEN 1 ELSE 2 END,
                     e.並び順, CAST(e.Number AS INTEGER)
            """;
        using var r = cmd.ExecuteReader();
        string S(int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i)) ?? "";
        while (r.Read())
        {
            int.TryParse(S(0), out int num);
            string syutai = S(4);
            list.Add(new YearEndTarget(num, S(1), S(2), S(3), syutai is "3" or "4"));
        }
        return list;
    }

    /// <summary>レコードが無ければ作成し、給与・賞与トランから年間集計を再計算してから読み込む。</summary>
    public YearEndEntry LoadOrCreate(int number)
    {
        EnsureRow(number);
        Aggregate(number);
        return Load(number)!;
    }

    private void EnsureRow(int number)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO \"T_年末調整\" (Number) VALUES ($n)";
        cmd.Parameters.AddWithValue("$n", number.ToString());
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 給与・賞与トランから年間集計列を再計算して保存する（入力列・結果列は変更しない）。
    /// 現行 F_年調_従業員情報：給与N月＝総支給額−非課税通勤費−非課税金額計、社保/源泉/扶養親族数はそのまま。
    /// </summary>
    public void Aggregate(int number)
    {
        var 給与 = new int[12]; var 社保 = new int[12]; var 源泉 = new int[12]; var 扶 = new int[12];
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT 支給回, 総支給額, 非課税通勤費, 非課税金額計, 社会保険合計, 所得税, 扶養親族数 FROM \"T_給与トラン\" WHERE Number=$n";
            cmd.Parameters.AddWithValue("$n", number.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int kai = GetInt(r, 0);
                if (kai < 1 || kai > 12) continue;
                給与[kai - 1] = GetInt(r, 1) - GetInt(r, 2) - GetInt(r, 3);
                社保[kai - 1] = GetInt(r, 4);
                源泉[kai - 1] = GetInt(r, 5);
                扶[kai - 1] = GetInt(r, 6);
            }
        }

        var 賞 = new int[4]; var 社賞 = new int[4]; var 源賞 = new int[4]; var 扶賞 = new int[4];
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT 回, 総支給額, 社会保険合計, 所得税, 扶養親族数 FROM \"T_賞与トラン\" WHERE Number=$n";
            cmd.Parameters.AddWithValue("$n", number.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int kai = GetInt(r, 0);
                if (kai < 1 || kai > 4) continue;
                賞[kai - 1] = GetInt(r, 1);
                社賞[kai - 1] = GetInt(r, 2);
                源賞[kai - 1] = GetInt(r, 3);
                扶賞[kai - 1] = GetInt(r, 4);
            }
        }

        int 給与計 = 給与.Sum(), 給与社保計 = 社保.Sum(), 給与源泉計 = 源泉.Sum();
        int 賞与計 = 賞.Sum(), 賞与社保計 = 社賞.Sum(), 賞与源泉計 = 源賞.Sum();

        var sets = new List<string>();
        var ps = new List<(string p, object v)>();
        void Add(string col, int val) { string p = "$p" + ps.Count; sets.Add($"\"{col}\"={p}"); ps.Add((p, val)); }
        for (int i = 0; i < 12; i++)
        {
            Add($"給与{i + 1}月", 給与[i]); Add($"社保{i + 1}月", 社保[i]);
            Add($"源泉{i + 1}月", 源泉[i]); Add($"給与扶養親族数{i + 1}月", 扶[i]);
        }
        for (int i = 0; i < 4; i++)
        {
            Add($"賞与{i + 1}", 賞[i]); Add($"社保賞与{i + 1}", 社賞[i]);
            Add($"源泉賞与{i + 1}", 源賞[i]); Add($"賞与扶養親族数{i + 1}回目", 扶賞[i]);
        }
        Add("給与計", 給与計); Add("給与社保計", 給与社保計); Add("給与源泉計", 給与源泉計);
        Add("賞与計", 賞与計); Add("賞与社保計", 賞与社保計); Add("賞与源泉計", 賞与源泉計);
        Add("給与賞与計", 給与計 + 賞与計); Add("源泉計", 給与源泉計 + 賞与源泉計);

        using var up = _conn.CreateCommand();
        up.CommandText = $"UPDATE \"T_年末調整\" SET {string.Join(", ", sets)} WHERE Number=$n";
        foreach (var (p, v) in ps) up.Parameters.AddWithValue(p, v);
        up.Parameters.AddWithValue("$n", number.ToString());
        up.ExecuteNonQuery();
    }

    /// <summary>1件読み込む（全タブ項目）。無ければ null。</summary>
    public YearEndEntry? Load(int number)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM \"T_年末調整\" WHERE Number=$n";
        cmd.Parameters.AddWithValue("$n", number.ToString());
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        int I(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? 0 : Convert.ToInt32(r.GetValue(o)); }
        string S(string c) { int o = r.GetOrdinal(c); return r.IsDBNull(o) ? "" : Convert.ToString(r.GetValue(o)) ?? ""; }
        bool B(string c) => S(c) == "1";
        string Code(string c, string def) { var v = S(c); return string.IsNullOrEmpty(v) ? def : v; }

        var e = new YearEndEntry
        {
            Number = number,
            乙欄 = Code("乙欄", "1"),
            源泉徴収票提出 = B("源泉徴収票提出"),
            源泉徴収票摘要欄追記文 = S("源泉徴収票摘要欄追記文"),

            住民税コード = Code("住民税コード", "1"),
            寡婦 = Code("寡婦", "3"), 障害 = Code("障害", "3"),
            未成年 = B("未成年"), 勤労学生 = B("勤労学生"), 死亡退職 = B("死亡退職"),
            災害者 = B("災害者"), 外国人 = B("外国人"),
            所得金額調整控除適用 = B("所得金額調整控除適用区分"),
            所得金額調整控除適用の扶養者氏名 = S("所得金額調整控除適用の扶養者氏名"),

            就職 = B("就職"), 中途就職日 = S("中途就職日"),
            退職 = B("退職"), 中途退職日 = S("中途退職日"),
            前社の名称 = S("前社の名称"), 所在地 = S("所在地"),
            前社収入 = I("前社収入"), 前社社保 = I("前社社保"), 前社源泉 = I("前社源泉"),
            前社退職日 = S("前社退職日"),

            非課税通勤手当金額 = I("金額"), 非課税通勤手当算出根拠 = S("算出根拠"),

            新生命保険 = I("新生命保険"), 生命保険 = I("生命保険"), 介護医療保険 = I("介護医療保険"),
            新個人年金 = I("新個人年金"), 個人年金 = I("個人年金"),
            申告社保 = I("申告社保"), 国民年金保険料等 = I("国民年金保険料等"),
            地震保険 = I("地震保険"), 長期損害 = I("長期損害"), 小規模共済 = I("小規模共済"),

            住宅控除 = I("住宅控除"),
            新築購入借入金 = I("新築又は購入に係る借入金"),
            新築購入居住開始 = S("新築又は購入居住開始年月日"), 新築購入適用区分 = S("新築又は購入適用区分"),
            増改築借入金 = I("増改築等に係る借入金"),
            増改築居住開始 = S("増改築等居住開始年月日"), 増改築適用区分 = S("増改築等適用区分"),
            住宅借入金特別控除可能額 = I("住宅借入金特別控除可能額"),

            配偶者あり = !string.IsNullOrWhiteSpace(S("配偶者")),
            配偶者所得 = I("配偶者所得"),

            扶養控除等合計 = I("扶養控除等合計"),
            特定親族特別控除額 = I("特定親族特別控除額"),

            給与計 = I("給与計"), 給与社保計 = I("給与社保計"), 給与源泉計 = I("給与源泉計"),
            賞与計 = I("賞与計"), 賞与社保計 = I("賞与社保計"), 賞与源泉計 = I("賞与源泉計"),
            給与賞与計 = I("給与賞与計"), 源泉計 = I("源泉計"),

            給与所得控除後 = I("給与所得控除後"), 給与所得控除後調整控除後 = I("給与所得控除後調整控除後"),
            所得金額調整控除額 = I("所得金額調整控除額"), 生保控除 = I("生保控除"), 地震控除 = I("地震控除"),
            配偶者特別控除 = I("配偶者特別控除"), 基礎控除 = I("基礎控除"),
            所得控除計 = I("所得控除計"), 課税所得 = I("課税所得"), 所得税 = I("所得税"),
            年調所得税額 = I("年調所得税額"), 年調年税額 = I("年調年税額"), 過不足額 = I("過不足額"),
        };

        e.配偶者 = new YearEndSpouse
        {
            氏名 = S("配偶者"), フリガナ = S("配偶者フリガナ"), 生年月日 = S("配偶者生年月日"),
            収入 = S("配偶者収入"), 収入種類 = S("配偶者収入種類"),
            障害 = Code("配偶者障害", "3"), 同居 = B("同居配偶者"), 非居住者 = B("配偶者非居住者"),
        };
        for (int i = 1; i <= 7; i++)
        {
            e.扶養親族[i - 1] = new YearEndDependent
            {
                氏名 = S($"扶養親族名前{i}"), フリガナ = S($"扶養親族{i}フリガナ"),
                生年月日 = S($"扶養親族生年月日{i}"), 続柄 = S($"扶養親族続柄{i}"),
                収入 = I($"扶養親族収入{i}"), 収入種類 = S($"扶養親族収入種類{i}"),
                障害 = Code($"扶養親族障害{i}", "3"), 同居 = B($"扶養親族同居{i}"),
                非居住者 = B($"扶養親族{i}非居住者"), 留学送金 = Code($"扶養親族留学送金区分{i}", "0"),
            };
        }
        for (int i = 0; i < 12; i++)
        {
            e.給与[i] = I($"給与{i + 1}月"); e.社保[i] = I($"社保{i + 1}月"); e.源泉[i] = I($"源泉{i + 1}月");
            e.給与扶養親族数[i] = I($"給与扶養親族数{i + 1}月");
        }
        for (int i = 0; i < 4; i++)
        {
            e.賞与[i] = I($"賞与{i + 1}"); e.社保賞与[i] = I($"社保賞与{i + 1}"); e.源泉賞与[i] = I($"源泉賞与{i + 1}");
            e.賞与扶養親族数[i] = I($"賞与扶養親族数{i + 1}回目");
        }
        return e;
    }

    /// <summary>入力列と計算結果列を全タブ分保存する（集計列は Aggregate が管理）。</summary>
    public void Save(YearEndEntry e)
    {
        var sets = new List<string>();
        var ps = new List<(string p, object v)>();
        void S(string col, string? v) { string p = "$p" + ps.Count; sets.Add($"\"{col}\"={p}"); ps.Add((p, (object?)v ?? "")); }
        void N(string col, int v) { string p = "$p" + ps.Count; sets.Add($"\"{col}\"={p}"); ps.Add((p, v)); }
        static string b(bool x) => x ? "1" : "0";

        // 共通区分
        S("乙欄", e.乙欄); S("源泉徴収票提出", b(e.源泉徴収票提出)); S("源泉徴収票摘要欄追記文", e.源泉徴収票摘要欄追記文);
        // 本人区分
        S("住民税コード", e.住民税コード); S("寡婦", e.寡婦); S("障害", e.障害);
        S("未成年", b(e.未成年)); S("勤労学生", b(e.勤労学生)); S("死亡退職", b(e.死亡退職));
        S("災害者", b(e.災害者)); S("外国人", b(e.外国人));
        S("所得金額調整控除適用区分", b(e.所得金額調整控除適用)); S("所得金額調整控除適用の扶養者氏名", e.所得金額調整控除適用の扶養者氏名);
        // 転職
        S("就職", b(e.就職)); S("中途就職日", e.中途就職日); S("退職", b(e.退職)); S("中途退職日", e.中途退職日);
        S("前社の名称", e.前社の名称); S("所在地", e.所在地);
        N("前社収入", e.前社収入); N("前社社保", e.前社社保); N("前社源泉", e.前社源泉); S("前社退職日", e.前社退職日);
        // 非課税通勤手当の調整
        N("金額", e.非課税通勤手当金額); S("算出根拠", e.非課税通勤手当算出根拠);
        // 保険料等
        N("新生命保険", e.新生命保険); N("生命保険", e.生命保険); N("介護医療保険", e.介護医療保険);
        N("新個人年金", e.新個人年金); N("個人年金", e.個人年金); N("申告社保", e.申告社保);
        N("国民年金保険料等", e.国民年金保険料等); N("地震保険", e.地震保険); N("長期損害", e.長期損害); N("小規模共済", e.小規模共済);
        // 住宅借入金
        N("住宅控除", e.住宅控除); N("新築又は購入に係る借入金", e.新築購入借入金);
        S("新築又は購入居住開始年月日", e.新築購入居住開始); S("新築又は購入適用区分", e.新築購入適用区分);
        N("増改築等に係る借入金", e.増改築借入金); S("増改築等居住開始年月日", e.増改築居住開始); S("増改築等適用区分", e.増改築適用区分);
        N("住宅借入金特別控除可能額", e.住宅借入金特別控除可能額);
        // 配偶者
        S("配偶者", e.配偶者.氏名); S("配偶者フリガナ", e.配偶者.フリガナ); S("配偶者生年月日", e.配偶者.生年月日);
        S("配偶者収入", e.配偶者.収入); S("配偶者収入種類", e.配偶者.収入種類); S("配偶者障害", e.配偶者.障害);
        S("同居配偶者", b(e.配偶者.同居)); S("配偶者非居住者", b(e.配偶者.非居住者));
        N("配偶者所得", e.配偶者所得);
        // 扶養親族7人
        for (int i = 1; i <= 7; i++)
        {
            var d = e.扶養親族[i - 1];
            S($"扶養親族名前{i}", d.氏名); S($"扶養親族{i}フリガナ", d.フリガナ);
            S($"扶養親族生年月日{i}", d.生年月日); S($"扶養親族続柄{i}", d.続柄);
            N($"扶養親族収入{i}", d.収入); S($"扶養親族収入種類{i}", d.収入種類);
            S($"扶養親族障害{i}", d.障害); S($"扶養親族同居{i}", b(d.同居));
            S($"扶養親族{i}非居住者", b(d.非居住者)); S($"扶養親族留学送金区分{i}", d.留学送金);
        }
        // 計算結果
        N("扶養控除等合計", e.扶養控除等合計); N("特定親族特別控除額", e.特定親族特別控除額);
        N("給与所得控除後", e.給与所得控除後); N("給与所得控除後調整控除後", e.給与所得控除後調整控除後);
        N("所得金額調整控除額", e.所得金額調整控除額); N("生保控除", e.生保控除); N("地震控除", e.地震控除);
        N("配偶者特別控除", e.配偶者特別控除); N("基礎控除", e.基礎控除);
        N("所得控除計", e.所得控除計); N("課税所得", e.課税所得); N("所得税", e.所得税);
        N("年調所得税額", e.年調所得税額); N("年調年税額", e.年調年税額); N("過不足額", e.過不足額);

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE \"T_年末調整\" SET {string.Join(", ", sets)} WHERE Number=$n";
        foreach (var (p, v) in ps) cmd.Parameters.AddWithValue(p, v);
        cmd.Parameters.AddWithValue("$n", e.Number.ToString());
        cmd.ExecuteNonQuery();
    }

    private static int GetInt(SqliteDataReader r, int i)
        => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
