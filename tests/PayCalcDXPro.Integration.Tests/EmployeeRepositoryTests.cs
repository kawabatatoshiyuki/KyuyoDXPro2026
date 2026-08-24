using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Company;
using PayCalcDXPro.Data.Employee;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 従業員（T_従業員情報）Phase1の保存/読込/採番/一覧/削除と、
/// 基本情報UPDATEが他フェーズの列を保全することを検証する。Windows専用（DPAPI/SQLCipher）。
/// </summary>
public class EmployeeRepositoryTests : IDisposable
{
    private readonly string _dir;

    public EmployeeRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_emp_{Guid.NewGuid():N}");
    }

    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    [Fact]
    public void 基本情報が往復し採番される()
    {
        if (!OperatingSystem.IsWindows()) return;

        int number;
        using (var store = Open())
        {
            var repo = new EmployeeRepository(store.Connection);
            repo.EnsureSchema();
            number = repo.Save(new Employee
            {
                No = "001", 氏名 = "山田太郎", フリガナ = "ヤマダタロウ",
                性別コード = "1", 生年月日 = "1990/04/01",
                郵便番号 = "1000001", 住所１ = "東京都千代田区", 住所２ = "1-1",
                就退区分 = "1", 銀行番号 = "1", 預金種類 = "1", 口座番号 = "1234567",
            });
            Assert.Equal(1, number); // 空テーブルなら1から
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new EmployeeRepository(reopened.Connection);
            repo.EnsureSchema();
            var e = repo.Load(number)!;
            Assert.Equal("001", e.No);
            Assert.Equal("山田太郎", e.氏名);
            Assert.Equal("ヤマダタロウ", e.フリガナ);
            Assert.Equal("1990/04/01", e.生年月日);
            Assert.Equal("1000001", e.郵便番号);
            Assert.Equal("1234567", e.口座番号);
            Assert.Equal(1, e.並び順); // 未指定なら採番Numberと同じ
        }
    }

    [Fact]
    public void 基本情報の更新は他フェーズの列を保全する()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        int number = repo.Save(new Employee { No = "001", 氏名 = "佐藤花子", フリガナ = "サトウハナコ" });

        // 後続フェーズの列を直接セット（残業単価・報酬月額）。
        using (var cmd = store.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE T_従業員情報 SET 報酬月額=300000, 普通残業単価=1800, 健康保険='1' WHERE Number=$n";
            cmd.Parameters.AddWithValue("$n", number.ToString());
            cmd.ExecuteNonQuery();
        }

        // 基本情報のみ更新。
        var e = repo.Load(number)!;
        e.氏名 = "佐藤花";
        e.役職番号 = "0";
        repo.Save(e);

        using (var cmd = store.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT 報酬月額, 普通残業単価, 健康保険, 氏名 FROM T_従業員情報 WHERE Number=$n";
            cmd.Parameters.AddWithValue("$n", number.ToString());
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(300000L, r.GetInt64(0));
            Assert.Equal(1800L, r.GetInt64(1));
            Assert.Equal("1", r.GetString(2));
            Assert.Equal("佐藤花", r.GetString(3)); // 基本情報は更新済み
        }
    }

    [Fact]
    public void 社会保険_税区分が往復する()
    {
        if (!OperatingSystem.IsWindows()) return;

        int number;
        using (var store = Open())
        {
            var repo = new EmployeeRepository(store.Connection);
            repo.EnsureSchema();
            number = repo.Save(new Employee
            {
                No = "001", 氏名 = "鈴木一郎", フリガナ = "スズキイチロウ",
                健康保険 = "1", 介護保険 = "1", 子育支援 = "2", 厚生年金 = "1", 年金基金 = "2", 雇用保険 = "1",
                所得税源泉 = "2", 本人障害者 = "3", 本人寡婦夫 = "2", 本人勤労学生 = "2",
            });
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new EmployeeRepository(reopened.Connection);
            repo.EnsureSchema();
            var e = repo.Load(number)!;
            Assert.Equal("1", e.健康保険);
            Assert.Equal("1", e.介護保険);
            Assert.Equal("2", e.子育支援);
            Assert.Equal("2", e.年金基金);
            Assert.Equal("2", e.所得税源泉);
            Assert.Equal("3", e.本人障害者);
            Assert.Equal("2", e.本人寡婦夫);
            Assert.Equal("2", e.本人勤労学生);
        }
    }

    [Fact]
    public void 配偶者_扶養_住民税が往復し源泉数が自動計算される()
    {
        if (!OperatingSystem.IsWindows()) return;

        int number;
        using (var store = Open())
        {
            var repo = new EmployeeRepository(store.Connection);
            repo.EnsureSchema();
            number = repo.Save(new Employee
            {
                No = "001", 氏名 = "扶養持太郎", フリガナ = "フヨウモチタロウ",
                配偶者合計所得見積 = "1", 配偶者区分 = "2",       // +1
                扶養親族親族数 = 2, 扶養親族一般障害 = 1,          // +3
                住民税1_5月 = 12000, 住民税6月 = 15000, 住民税7_12月 = 12000,
            });
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new EmployeeRepository(reopened.Connection);
            repo.EnsureSchema();
            var e = repo.Load(number)!;
            Assert.Equal("2", e.配偶者区分);
            Assert.Equal(2, e.扶養親族親族数);
            Assert.Equal(1, e.扶養親族一般障害);
            Assert.Equal(12000, e.住民税1_5月);
            Assert.Equal(15000, e.住民税6月);
            Assert.Equal(4, e.源泉税額計算の扶養親族数); // +1(配偶者)+3(扶養2+一般障害1)
        }
    }

    [Fact]
    public void 給与_割増賃金が往復し賞与累計は保全される()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        int number = repo.Save(new Employee
        {
            No = "001", 氏名 = "給与太郎", フリガナ = "キュウヨタロウ",
            報酬月額 = 300000, 日給単価 = 12000,
            法定内残業単価 = 1500, 普通残業単価 = 1875, 普通残業単価60h超 = 2250,
            深夜残業単価 = 2250, 深夜残業単価60h超 = 2625,
            休日残業単価 = 2025, 休日残業単価60h超 = 2400,
            休日深夜残業単価 = 2775, 休日深夜残業単価60h超 = 3150,
        });

        // 賞与累計は賞与処理が更新する想定（フォーム外）。直接セットして保全されることを確認。
        using (var cmd = store.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE T_従業員情報 SET 賞与累計=500000 WHERE Number=$n";
            cmd.Parameters.AddWithValue("$n", number.ToString());
            cmd.ExecuteNonQuery();
        }

        var e = repo.Load(number)!;
        e.報酬月額 = 320000; // 基本情報の更新
        repo.Save(e);

        var loaded = repo.Load(number)!;
        Assert.Equal(320000, loaded.報酬月額);
        Assert.Equal(12000, loaded.日給単価);
        Assert.Equal(1875, loaded.普通残業単価);
        Assert.Equal(3150, loaded.休日深夜残業単価60h超);

        long bonus;
        using (var cmd = store.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT 賞与累計 FROM T_従業員情報 WHERE Number=$n";
            cmd.Parameters.AddWithValue("$n", number.ToString());
            bonus = (long)cmd.ExecuteScalar()!;
        }
        Assert.Equal(500000, bonus); // 賞与累計は更新で保全される
    }

    [Fact]
    public void 一覧は部門名_役職名_就退区分名を結合して返す()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        // 部門・役職マスタを用意（番号1=営業部 / 番号1=課長）。
        var company = new CompanyRepository(store.Connection);
        company.EnsureSchema();
        var masters = new CompanyMasters();
        masters.部門.Add(new DepartmentRow { 部門名 = "営業部" });
        masters.役職.Add(new PositionRow { 役職名 = "課長" });
        company.SaveMasters(masters);

        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(new Employee { No = "001", 氏名 = "山田太郎", フリガナ = "ヤマダタロウ", 部門番号 = "1", 役職番号 = "1", 就退区分 = "3" });

        var list = repo.List();
        var item = Assert.Single(list);
        Assert.Equal("営業部", item.部門名);
        Assert.Equal("課長", item.役職名);
        Assert.Equal("本年退職", item.就退区分名); // M_コード 014
    }

    [Fact]
    public void 採番は最大プラス1で削除も効く()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        int n1 = repo.Save(new Employee { No = "001", 氏名 = "A", フリガナ = "エー" });
        int n2 = repo.Save(new Employee { No = "002", 氏名 = "B", フリガナ = "ビー" });
        Assert.Equal(1, n1);
        Assert.Equal(2, n2);
        Assert.Equal(3, repo.NextNumber());

        repo.Delete(n1);
        Assert.Null(repo.Load(n1));
        Assert.Single(repo.List());
    }

    [Fact]
    public void 退職者は一覧の末尾に退職フラグ付きで並ぶ()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        // 退職者を先に登録しても末尾へ回る。
        repo.Save(new Employee { No = "001", 氏名 = "退職太郎", フリガナ = "タイショクタロウ", 就退区分 = "3", 就退年月日 = "2026/03/31" });
        repo.Save(new Employee { No = "002", 氏名 = "在籍花子", フリガナ = "ザイセキハナコ", 就退区分 = "1" });
        repo.Save(new Employee { No = "003", 氏名 = "中途次郎", フリガナ = "チュウトジロウ", 就退区分 = "2", 就退年月日 = "2026/05/01" });

        var list = repo.List();
        // 在籍(1/2)が先、退職(3/4)が後ろ。
        Assert.Equal(new[] { "在籍花子", "中途次郎", "退職太郎" }, list.Select(x => x.氏名));
        Assert.False(list[0].退職);
        Assert.False(list[1].退職);
        Assert.True(list[2].退職);
    }

    [Fact]
    public void 並び順の更新が一覧の順序に反映される()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new EmployeeRepository(store.Connection);
        repo.EnsureSchema();
        int a = repo.Save(new Employee { No = "001", 氏名 = "A", フリガナ = "エー" });
        int b = repo.Save(new Employee { No = "002", 氏名 = "B", フリガナ = "ビー" });
        int c = repo.Save(new Employee { No = "003", 氏名 = "C", フリガナ = "シー" });

        // 既定は登録順（A,B,C）。
        Assert.Equal(new[] { "A", "B", "C" }, repo.List().Select(x => x.氏名));

        // C,A,B の順に並べ替え。
        repo.UpdateOrder(new[] { c, a, b });
        Assert.Equal(new[] { "C", "A", "B" }, repo.List().Select(x => x.氏名));

        // 並べ替え後の新規は末尾に付く。
        repo.Save(new Employee { No = "004", 氏名 = "D", フリガナ = "ディー" });
        Assert.Equal(new[] { "C", "A", "B", "D" }, repo.List().Select(x => x.氏名));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* 一時フォルダ削除失敗は無視 */ }
        }
    }
}
