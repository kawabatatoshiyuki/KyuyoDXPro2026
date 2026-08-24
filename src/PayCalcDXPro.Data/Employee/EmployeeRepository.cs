using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.Employee;

/// <summary>
/// 従業員（T_従業員情報）の読み書き。現行 F_従業員情報入力 のロジックをパラメータ化SQLで移植。
/// Phase1は基本情報のみを扱い、UPDATEでは他フェーズの列を変更しない。
/// </summary>
public sealed class EmployeeRepository
{
    private readonly SqliteConnection _conn;

    public EmployeeRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>T_従業員情報・M_コードを用意し、性別(017)・就退区分(014)を必要なら初期投入する。</summary>
    public void EnsureSchema()
    {
        // 現行スキーマに一致（番号・コード系はTEXT、カウント/単価系はINTEGER）。
        Exec("""
            CREATE TABLE IF NOT EXISTS "T_従業員情報" (
                "Number" TEXT, "No" TEXT, "氏名" TEXT, "フリガナ" TEXT,
                "郵便番号" TEXT, "住所１" TEXT, "住所２" TEXT,
                "部門番号" TEXT, "役職番号" TEXT, "性別コード" TEXT, "生年月日" TEXT,
                "就退区分" TEXT, "就退年月日" TEXT,
                "健康保険" TEXT, "介護保険" TEXT, "子育支援" TEXT, "厚生年金" TEXT, "年金基金" TEXT,
                "雇用保険" TEXT, "所得税源泉" TEXT,
                "本人障害者" TEXT, "本人寡婦夫" TEXT, "本人勤労学生" TEXT,
                "配偶者区分" TEXT, "配偶者障害者" TEXT, "配偶者合計所得見積" TEXT,
                "扶養親族親族数" INTEGER, "扶養親族同居老親" INTEGER, "扶養親族老人" INTEGER,
                "扶養親族特定" INTEGER, "扶養親族同居特別" INTEGER, "扶養親族特別障害" INTEGER,
                "扶養親族一般障害" INTEGER, "源泉税額計算の扶養親族数" INTEGER, "特定親族数" INTEGER,
                "住民税1_5月" INTEGER, "住民税6月" INTEGER, "住民税7_12月" INTEGER,
                "法定内残業単価" INTEGER, "普通残業単価" INTEGER, "普通残業単価60h超" INTEGER,
                "深夜残業単価" INTEGER, "深夜残業単価60h超" INTEGER,
                "休日残業単価" INTEGER, "休日残業単価60h超" INTEGER,
                "休日深夜残業単価" INTEGER, "休日深夜残業単価60h超" INTEGER,
                "報酬月額" INTEGER, "日給単価" INTEGER, "賞与累計" INTEGER,
                "銀行番号" TEXT, "預金種類" TEXT, "口座番号" TEXT, "並び順" INTEGER,
                PRIMARY KEY("Number")
            );
            """);
        Exec("""CREATE TABLE IF NOT EXISTS "M_コード" ("項目区分" TEXT, "コード" TEXT, "値" TEXT);""");
        // 一覧のJOIN先。会社情報未設定でも一覧が動くよう存在保証する（中身はCompanyRepositoryが管理）。
        Exec("""CREATE TABLE IF NOT EXISTS "M_部門情報" ("部門番号" TEXT, "部門名" TEXT, PRIMARY KEY("部門番号"));""");
        Exec("""CREATE TABLE IF NOT EXISTS "M_役職情報" ("役職番号" TEXT, "役職名" TEXT, PRIMARY KEY("役職番号"));""");

        SeedCodesIfMissing("014", ("1", "昨年から継続勤務"), ("2", "本年に中途就職"), ("3", "本年退職"), ("4", "本年死亡退職"));
        SeedCodesIfMissing("017", ("1", "男"), ("2", "女"));
        // Phase2：税区分のコードマスタ
        SeedCodesIfMissing("010", ("1", "甲欄"), ("2", "乙欄"), ("3", "計算しない"));
        SeedCodesIfMissing("015", ("1", "該当しない"), ("2", "一般"), ("3", "特別"));
        SeedCodesIfMissing("006", ("1", "該当しない"), ("2", "寡婦"), ("3", "ひとり親"));
        SeedCodesIfMissing("008", ("1", "該当しない"), ("2", "該当する"));
        // Phase3：配偶者関連のコードマスタ
        SeedCodesIfMissing("001", ("1", "いない"), ("2", "源泉控除対象配偶者"), ("3", "同一生計配偶者"), ("4", "控除対象外"));
        SeedCodesIfMissing("019", ("1", "該当しない"), ("2", "一般"), ("3", "特別"), ("4", "同居特別"));
        SeedCodesIfMissing("013", ("1", "900万円以下"), ("2", "900万円超"));
    }

    /// <summary>一覧（部門名・役職名・就退区分名を結合）。並び順→Number昇順。</summary>
    public IReadOnlyList<EmployeeListItem> List()
    {
        var list = new List<EmployeeListItem>();
        using var cmd = _conn.CreateCommand();
        // 現行同様、在籍(就退区分1/2)を先・退職(3/4)を後ろにグループ化し、各グループ内は並び順。
        cmd.CommandText = """
            SELECT e.Number, e.No, e.氏名, e.フリガナ,
                   IFNULL(b.部門名,'') AS 部門名, IFNULL(y.役職名,'') AS 役職名,
                   IFNULL(s.値,'') AS 就退区分名, e.就退区分
            FROM T_従業員情報 e
            LEFT JOIN M_部門情報 b ON b.部門番号 = e.部門番号
            LEFT JOIN M_役職情報 y ON y.役職番号 = e.役職番号
            LEFT JOIN M_コード s ON s.項目区分 = '014' AND s.コード = e.就退区分
            ORDER BY CASE WHEN e.就退区分 IN ('1','2') THEN 1 ELSE 2 END,
                     e.並び順, CAST(e.Number AS INTEGER)
            """;
        using var r = cmd.ExecuteReader();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        while (r.Read())
        {
            int.TryParse(S(0), out int num);
            string syutai = S(7);
            bool retired = syutai is "3" or "4";
            list.Add(new EmployeeListItem(num, S(1), S(2), S(3), S(4), S(5), S(6), retired));
        }
        return list;
    }

    /// <summary>1件読み込む（基本情報のみ）。無ければ null。</summary>
    public Employee? Load(int number)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT No, 氏名, フリガナ, 郵便番号, 住所１, 住所２, 部門番号, 役職番号, 性別コード,
                   生年月日, 就退区分, 就退年月日, 銀行番号, 預金種類, 口座番号, 並び順,
                   健康保険, 介護保険, 子育支援, 厚生年金, 年金基金, 雇用保険,
                   所得税源泉, 本人障害者, 本人寡婦夫, 本人勤労学生,
                   配偶者区分, 配偶者障害者, 配偶者合計所得見積,
                   扶養親族親族数, 扶養親族同居老親, 扶養親族老人, 扶養親族特定,
                   扶養親族同居特別, 扶養親族特別障害, 扶養親族一般障害, 特定親族数,
                   源泉税額計算の扶養親族数, 住民税1_5月, 住民税6月, 住民税7_12月,
                   報酬月額, 日給単価, 法定内残業単価, 普通残業単価, 普通残業単価60h超,
                   深夜残業単価, 深夜残業単価60h超, 休日残業単価, 休日残業単価60h超,
                   休日深夜残業単価, 休日深夜残業単価60h超
            FROM T_従業員情報 WHERE Number = $num
            """;
        cmd.Parameters.AddWithValue("$num", number.ToString());
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        int I(int i) => r.IsDBNull(i) ? 0 : (int)r.GetInt64(i);
        return new Employee
        {
            Number = number,
            No = S(0), 氏名 = S(1), フリガナ = S(2),
            郵便番号 = S(3), 住所１ = S(4), 住所２ = S(5),
            部門番号 = string.IsNullOrEmpty(S(6)) ? "0" : S(6),
            役職番号 = string.IsNullOrEmpty(S(7)) ? "0" : S(7),
            性別コード = string.IsNullOrEmpty(S(8)) ? "1" : S(8),
            生年月日 = S(9),
            就退区分 = string.IsNullOrEmpty(S(10)) ? "1" : S(10),
            就退年月日 = S(11),
            銀行番号 = string.IsNullOrEmpty(S(12)) ? "1" : S(12),
            預金種類 = string.IsNullOrEmpty(S(13)) ? "1" : S(13),
            口座番号 = S(14),
            並び順 = r.IsDBNull(15) ? 0 : (int)r.GetInt64(15),
            健康保険 = D(S(16), "1"), 介護保険 = D(S(17), "2"), 子育支援 = D(S(18), "1"),
            厚生年金 = D(S(19), "1"), 年金基金 = D(S(20), "2"), 雇用保険 = D(S(21), "1"),
            所得税源泉 = D(S(22), "1"), 本人障害者 = D(S(23), "1"),
            本人寡婦夫 = D(S(24), "1"), 本人勤労学生 = D(S(25), "1"),
            配偶者区分 = D(S(26), "1"), 配偶者障害者 = D(S(27), "1"), 配偶者合計所得見積 = D(S(28), "1"),
            扶養親族親族数 = I(29), 扶養親族同居老親 = I(30), 扶養親族老人 = I(31), 扶養親族特定 = I(32),
            扶養親族同居特別 = I(33), 扶養親族特別障害 = I(34), 扶養親族一般障害 = I(35), 特定親族数 = I(36),
            源泉税額計算の扶養親族数 = I(37), 住民税1_5月 = I(38), 住民税6月 = I(39), 住民税7_12月 = I(40),
            報酬月額 = I(41), 日給単価 = I(42),
            法定内残業単価 = I(43), 普通残業単価 = I(44), 普通残業単価60h超 = I(45),
            深夜残業単価 = I(46), 深夜残業単価60h超 = I(47),
            休日残業単価 = I(48), 休日残業単価60h超 = I(49),
            休日深夜残業単価 = I(50), 休日深夜残業単価60h超 = I(51),
        };

        static string D(string v, string def) => string.IsNullOrEmpty(v) ? def : v;
    }

    /// <summary>採番（既存Numberの最大+1）。</summary>
    public int NextNumber()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(MAX(CAST(Number AS INTEGER)), 0) + 1 FROM T_従業員情報";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>新規の並び順（既存並び順の最大+1）。末尾に追加するため。</summary>
    public int NextOrder()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(MAX(並び順), 0) + 1 FROM T_従業員情報";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>一覧のドラッグ結果（Numberの並び）を 並び順=1..N として保存する。</summary>
    public void UpdateOrder(IReadOnlyList<int> orderedNumbers)
    {
        using var tx = _conn.BeginTransaction();
        for (int i = 0; i < orderedNumbers.Count; i++)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE T_従業員情報 SET 並び順 = $o WHERE Number = $n";
            cmd.Parameters.AddWithValue("$o", i + 1);
            cmd.Parameters.AddWithValue("$n", orderedNumbers[i].ToString());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>保存（新規=INSERT、既存=管理列のみUPDATE）。採番したNumberを返す。</summary>
    public int Save(Employee e)
    {
        // 源泉徴収税額表の扶養親族数は保存時に自動計算（現行同様）。
        e.源泉税額計算の扶養親族数 = EmployeeCalc.源泉扶養親族数(e);

        if (e.Number <= 0)
        {
            int number = NextNumber();
            int order = e.並び順 > 0 ? e.並び順 : NextOrder();
            using var ins = _conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO T_従業員情報
                    (Number, No, 氏名, フリガナ, 郵便番号, 住所１, 住所２, 部門番号, 役職番号,
                     性別コード, 生年月日, 就退区分, 就退年月日, 銀行番号, 預金種類, 口座番号, 並び順,
                     健康保険, 介護保険, 子育支援, 厚生年金, 年金基金, 雇用保険,
                     所得税源泉, 本人障害者, 本人寡婦夫, 本人勤労学生,
                     配偶者区分, 配偶者障害者, 配偶者合計所得見積,
                     扶養親族親族数, 扶養親族同居老親, 扶養親族老人, 扶養親族特定,
                     扶養親族同居特別, 扶養親族特別障害, 扶養親族一般障害, 特定親族数,
                     源泉税額計算の扶養親族数, 住民税1_5月, 住民税6月, 住民税7_12月,
                     報酬月額, 日給単価, 法定内残業単価, 普通残業単価, 普通残業単価60h超,
                     深夜残業単価, 深夜残業単価60h超, 休日残業単価, 休日残業単価60h超,
                     休日深夜残業単価, 休日深夜残業単価60h超)
                VALUES
                    ($Number, $No, $氏名, $フリガナ, $郵便番号, $住所1, $住所2, $部門番号, $役職番号,
                     $性別コード, $生年月日, $就退区分, $就退年月日, $銀行番号, $預金種類, $口座番号, $並び順,
                     $健康保険, $介護保険, $子育支援, $厚生年金, $年金基金, $雇用保険,
                     $所得税源泉, $本人障害者, $本人寡婦夫, $本人勤労学生,
                     $配偶者区分, $配偶者障害者, $配偶者合計所得見積,
                     $扶養親族親族数, $扶養親族同居老親, $扶養親族老人, $扶養親族特定,
                     $扶養親族同居特別, $扶養親族特別障害, $扶養親族一般障害, $特定親族数,
                     $源泉税額計算の扶養親族数, $住民税1_5月, $住民税6月, $住民税7_12月,
                     $報酬月額, $日給単価, $法定内残業単価, $普通残業単価, $普通残業単価60h超,
                     $深夜残業単価, $深夜残業単価60h超, $休日残業単価, $休日残業単価60h超,
                     $休日深夜残業単価, $休日深夜残業単価60h超)
                """;
            ins.Parameters.AddWithValue("$Number", number.ToString());
            BindBasic(ins, e);
            ins.Parameters.AddWithValue("$並び順", order);
            ins.ExecuteNonQuery();
            return number;
        }

        using var up = _conn.CreateCommand();
        up.CommandText = """
            UPDATE T_従業員情報 SET
                No=$No, 氏名=$氏名, フリガナ=$フリガナ, 郵便番号=$郵便番号, 住所１=$住所1, 住所２=$住所2,
                部門番号=$部門番号, 役職番号=$役職番号, 性別コード=$性別コード, 生年月日=$生年月日,
                就退区分=$就退区分, 就退年月日=$就退年月日, 銀行番号=$銀行番号, 預金種類=$預金種類,
                口座番号=$口座番号,
                健康保険=$健康保険, 介護保険=$介護保険, 子育支援=$子育支援, 厚生年金=$厚生年金,
                年金基金=$年金基金, 雇用保険=$雇用保険, 所得税源泉=$所得税源泉,
                本人障害者=$本人障害者, 本人寡婦夫=$本人寡婦夫, 本人勤労学生=$本人勤労学生,
                配偶者区分=$配偶者区分, 配偶者障害者=$配偶者障害者, 配偶者合計所得見積=$配偶者合計所得見積,
                扶養親族親族数=$扶養親族親族数, 扶養親族同居老親=$扶養親族同居老親, 扶養親族老人=$扶養親族老人,
                扶養親族特定=$扶養親族特定, 扶養親族同居特別=$扶養親族同居特別, 扶養親族特別障害=$扶養親族特別障害,
                扶養親族一般障害=$扶養親族一般障害, 特定親族数=$特定親族数,
                源泉税額計算の扶養親族数=$源泉税額計算の扶養親族数,
                住民税1_5月=$住民税1_5月, 住民税6月=$住民税6月, 住民税7_12月=$住民税7_12月,
                報酬月額=$報酬月額, 日給単価=$日給単価,
                法定内残業単価=$法定内残業単価, 普通残業単価=$普通残業単価, 普通残業単価60h超=$普通残業単価60h超,
                深夜残業単価=$深夜残業単価, 深夜残業単価60h超=$深夜残業単価60h超,
                休日残業単価=$休日残業単価, 休日残業単価60h超=$休日残業単価60h超,
                休日深夜残業単価=$休日深夜残業単価, 休日深夜残業単価60h超=$休日深夜残業単価60h超
            WHERE Number=$Number
            """;
        up.Parameters.AddWithValue("$Number", e.Number.ToString());
        BindBasic(up, e);
        up.ExecuteNonQuery();
        return e.Number;
    }

    public void Delete(int number)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM T_従業員情報 WHERE Number = $num";
        cmd.Parameters.AddWithValue("$num", number.ToString());
        cmd.ExecuteNonQuery();
    }

    // 並び順以外の基本情報パラメータを束ねる。
    private static void BindBasic(SqliteCommand cmd, Employee e)
    {
        cmd.Parameters.AddWithValue("$No", e.No ?? "");
        cmd.Parameters.AddWithValue("$氏名", e.氏名 ?? "");
        cmd.Parameters.AddWithValue("$フリガナ", e.フリガナ ?? "");
        cmd.Parameters.AddWithValue("$郵便番号", e.郵便番号 ?? "");
        cmd.Parameters.AddWithValue("$住所1", e.住所１ ?? "");
        cmd.Parameters.AddWithValue("$住所2", e.住所２ ?? "");
        cmd.Parameters.AddWithValue("$部門番号", e.部門番号 ?? "0");
        cmd.Parameters.AddWithValue("$役職番号", e.役職番号 ?? "0");
        cmd.Parameters.AddWithValue("$性別コード", e.性別コード ?? "1");
        cmd.Parameters.AddWithValue("$生年月日", e.生年月日 ?? "");
        cmd.Parameters.AddWithValue("$就退区分", e.就退区分 ?? "1");
        cmd.Parameters.AddWithValue("$就退年月日", e.就退年月日 ?? "");
        cmd.Parameters.AddWithValue("$銀行番号", e.銀行番号 ?? "1");
        cmd.Parameters.AddWithValue("$預金種類", e.預金種類 ?? "1");
        cmd.Parameters.AddWithValue("$口座番号", e.口座番号 ?? "");
        cmd.Parameters.AddWithValue("$健康保険", e.健康保険 ?? "1");
        cmd.Parameters.AddWithValue("$介護保険", e.介護保険 ?? "2");
        cmd.Parameters.AddWithValue("$子育支援", e.子育支援 ?? "1");
        cmd.Parameters.AddWithValue("$厚生年金", e.厚生年金 ?? "1");
        cmd.Parameters.AddWithValue("$年金基金", e.年金基金 ?? "2");
        cmd.Parameters.AddWithValue("$雇用保険", e.雇用保険 ?? "1");
        cmd.Parameters.AddWithValue("$所得税源泉", e.所得税源泉 ?? "1");
        cmd.Parameters.AddWithValue("$本人障害者", e.本人障害者 ?? "1");
        cmd.Parameters.AddWithValue("$本人寡婦夫", e.本人寡婦夫 ?? "1");
        cmd.Parameters.AddWithValue("$本人勤労学生", e.本人勤労学生 ?? "1");
        cmd.Parameters.AddWithValue("$配偶者区分", e.配偶者区分 ?? "1");
        cmd.Parameters.AddWithValue("$配偶者障害者", e.配偶者障害者 ?? "1");
        cmd.Parameters.AddWithValue("$配偶者合計所得見積", e.配偶者合計所得見積 ?? "1");
        cmd.Parameters.AddWithValue("$扶養親族親族数", e.扶養親族親族数);
        cmd.Parameters.AddWithValue("$扶養親族同居老親", e.扶養親族同居老親);
        cmd.Parameters.AddWithValue("$扶養親族老人", e.扶養親族老人);
        cmd.Parameters.AddWithValue("$扶養親族特定", e.扶養親族特定);
        cmd.Parameters.AddWithValue("$扶養親族同居特別", e.扶養親族同居特別);
        cmd.Parameters.AddWithValue("$扶養親族特別障害", e.扶養親族特別障害);
        cmd.Parameters.AddWithValue("$扶養親族一般障害", e.扶養親族一般障害);
        cmd.Parameters.AddWithValue("$特定親族数", e.特定親族数);
        cmd.Parameters.AddWithValue("$源泉税額計算の扶養親族数", e.源泉税額計算の扶養親族数);
        cmd.Parameters.AddWithValue("$住民税1_5月", e.住民税1_5月);
        cmd.Parameters.AddWithValue("$住民税6月", e.住民税6月);
        cmd.Parameters.AddWithValue("$住民税7_12月", e.住民税7_12月);
        cmd.Parameters.AddWithValue("$報酬月額", e.報酬月額);
        cmd.Parameters.AddWithValue("$日給単価", e.日給単価);
        cmd.Parameters.AddWithValue("$法定内残業単価", e.法定内残業単価);
        cmd.Parameters.AddWithValue("$普通残業単価", e.普通残業単価);
        cmd.Parameters.AddWithValue("$普通残業単価60h超", e.普通残業単価60h超);
        cmd.Parameters.AddWithValue("$深夜残業単価", e.深夜残業単価);
        cmd.Parameters.AddWithValue("$深夜残業単価60h超", e.深夜残業単価60h超);
        cmd.Parameters.AddWithValue("$休日残業単価", e.休日残業単価);
        cmd.Parameters.AddWithValue("$休日残業単価60h超", e.休日残業単価60h超);
        cmd.Parameters.AddWithValue("$休日深夜残業単価", e.休日深夜残業単価);
        cmd.Parameters.AddWithValue("$休日深夜残業単価60h超", e.休日深夜残業単価60h超);
    }

    private void SeedCodesIfMissing(string category, params (string コード, string 値)[] items)
    {
        using (var check = _conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM M_コード WHERE 項目区分 = $c";
            check.Parameters.AddWithValue("$c", category);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return;
        }
        foreach (var (コード, 値) in items)
        {
            using var ins = _conn.CreateCommand();
            ins.CommandText = "INSERT INTO M_コード (項目区分, コード, 値) VALUES ($c, $k, $v)";
            ins.Parameters.AddWithValue("$c", category);
            ins.Parameters.AddWithValue("$k", コード);
            ins.Parameters.AddWithValue("$v", 値);
            ins.ExecuteNonQuery();
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
