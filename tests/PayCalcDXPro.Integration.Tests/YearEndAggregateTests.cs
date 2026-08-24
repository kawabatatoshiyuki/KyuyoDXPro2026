using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.YearEnd;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 年末調整データ層 <see cref="YearEndRepository"/> の年間集計（給与・賞与トラン → T_年末調整 の集計列）を
/// in-memory SQLite で確定的に検証する。現行 F_年調_従業員情報：給与N月＝総支給額−非課税通勤費−非課税金額計。
/// </summary>
public class YearEndAggregateTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public YearEndAggregateTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Exec("""CREATE TABLE "T_給与トラン" ("Number" TEXT,"支給回" INTEGER,"総支給額" INTEGER,"非課税通勤費" INTEGER,"非課税金額計" INTEGER,"社会保険合計" INTEGER,"所得税" INTEGER,"扶養親族数" INTEGER);""");
        Exec("""CREATE TABLE "T_賞与トラン" ("Number" TEXT,"回" INTEGER,"総支給額" INTEGER,"社会保険合計" INTEGER,"所得税" INTEGER,"扶養親族数" INTEGER);""");
        new YearEndRepository(_conn).EnsureSchema();
    }

    [Fact]
    public void 年間集計_給与賞与トランから各種計が正しく算出される()
    {
        // 給与1月：総30万・通勤1万・非課税1万 → 課税28万、社保4万、源泉5千
        InsertKyuyo("1", 1, 300000, 10000, 10000, 40000, 5000, 1);
        // 給与2月：総32万・通勤1万・非課税0 → 課税31万、社保4万、源泉6千
        InsertKyuyo("1", 2, 320000, 10000, 0, 40000, 6000, 1);
        // 賞与1回：総50万、社保7万、源泉3万
        InsertShouyo("1", 1, 500000, 70000, 30000, 1);

        var e = new YearEndRepository(_conn).LoadOrCreate(1);

        Assert.Equal(280000, e.給与[0]);
        Assert.Equal(310000, e.給与[1]);
        Assert.Equal(500000, e.賞与[0]);

        Assert.Equal(590000, e.給与計);
        Assert.Equal(80000, e.給与社保計);
        Assert.Equal(11000, e.給与源泉計);
        Assert.Equal(500000, e.賞与計);
        Assert.Equal(70000, e.賞与社保計);
        Assert.Equal(30000, e.賞与源泉計);
        Assert.Equal(1090000, e.給与賞与計);   // 590,000 + 500,000
        Assert.Equal(41000, e.源泉計);          // 11,000 + 30,000
    }

    [Fact]
    public void 年間集計_再実行しても入力列は保持され集計のみ更新される()
    {
        InsertKyuyo("2", 1, 300000, 0, 0, 40000, 5000, 0);
        var repo = new YearEndRepository(_conn);

        var e = repo.LoadOrCreate(2);
        e.介護医療保険 = 40000;
        e.扶養控除等合計 = 380000;
        repo.Save(e);

        // 給与トランを追加してから再集計
        InsertKyuyo("2", 2, 200000, 0, 0, 30000, 3000, 0);
        var e2 = repo.LoadOrCreate(2);

        Assert.Equal(500000, e2.給与計);       // 集計は更新
        Assert.Equal(40000, e2.介護医療保険);  // 入力は保持
        Assert.Equal(380000, e2.扶養控除等合計);
    }

    [Fact]
    public void 計再集計_前社分を加算し非課税通勤手当を控除する()
    {
        // トラン集計済みの状態を再現（給与 30万＋20万、社保 4万＋3万、源泉 5千＋3千、賞与 50万・社保7万・源泉3万）。
        var e = new YearEndEntry();
        e.給与[0] = 300000; e.給与[1] = 200000;
        e.社保[0] = 40000; e.社保[1] = 30000;
        e.源泉[0] = 5000; e.源泉[1] = 3000;
        e.賞与[0] = 500000; e.社保賞与[0] = 70000; e.源泉賞与[0] = 30000;
        // 本人情報タブの手入力：前社分と非課税通勤手当の調整。
        e.前社収入 = 100000; e.前社社保 = 12000; e.前社源泉 = 4000;
        e.非課税通勤手当金額 = 60000;

        YearEndCalculation.RecomputeTotals(e);

        Assert.Equal(540000, e.給与計);     // 100,000 + 500,000 − 60,000
        Assert.Equal(82000, e.給与社保計);  // 12,000 + 70,000
        Assert.Equal(12000, e.給与源泉計);  // 4,000 + 8,000
        Assert.Equal(500000, e.賞与計);
        Assert.Equal(70000, e.賞与社保計);
        Assert.Equal(30000, e.賞与源泉計);
        Assert.Equal(1040000, e.給与賞与計); // 540,000 + 500,000
        Assert.Equal(42000, e.源泉計);       // 12,000 + 30,000
    }

    [Fact]
    public void 年調しない_未済乙欄は結果欄がクリアされ計は残る()
    {
        // 既に年調結果が入っている状態（前回「する」で計算済み）を再現。
        var e = new YearEndEntry
        {
            乙欄 = "2", // 未済
            年調年税額 = 123400, 過不足額 = -5000, 基礎控除 = 580000, 課税所得 = 1000000,
            給与所得控除後 = 3000000, 所得控除計 = 900000, 配偶者特別控除 = 380000,
            配偶者あり = true, 扶養控除等合計 = 760000, 特定親族特別控除額 = 630000,
        };
        e.給与[0] = 300000; e.源泉[0] = 8000;

        YearEndCalculation.RecomputeTotals(e);
        YearEndCalculation.ClearYearEndResults(e);

        // 結果欄は全てクリア。
        Assert.Equal(0, e.年調年税額);
        Assert.Equal(0, e.過不足額);
        Assert.Equal(0, e.基礎控除);
        Assert.Equal(0, e.課税所得);
        Assert.Equal(0, e.給与所得控除後);
        Assert.Equal(0, e.所得控除計);
        Assert.Equal(0, e.配偶者特別控除);
        Assert.False(e.配偶者あり);
        Assert.Equal(0, e.扶養控除等合計);
        Assert.Equal(0, e.特定親族特別控除額);
        // 「計」は年調有無に関わらず残る（支給情報タブ表示用）。
        Assert.Equal(300000, e.給与計);
        Assert.Equal(8000, e.源泉計);
    }

    [Theory]
    [InlineData(60000, 0, 50000)]    // 地震保険料は上限5万
    [InlineData(30000, 0, 30000)]    // 地震保険料は支払額
    [InlineData(0, 8000, 8000)]      // 旧長期 ≤1万は全額
    [InlineData(0, 18000, 14000)]    // 旧長期 ≤2万は ×0.5+5,000
    [InlineData(0, 30000, 15000)]    // 旧長期 >2万は上限1.5万
    [InlineData(40000, 30000, 50000)]// 地震4万＋旧長期1.5万＝5.5万 → 合算上限5万
    public void 地震保険料控除_標準式どおり(int 地震, int 長期, int 期待)
        => Assert.Equal(期待, YearEndCalculation.地震保険料控除(地震, 長期));

    private void InsertKyuyo(string num, int 回, int 総, int 通勤, int 非課税, int 社保, int 源泉, int 扶養)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """INSERT INTO "T_給与トラン" VALUES ($n,$k,$s,$t,$h,$si,$g,$f);""";
        Bind(cmd, num, 回, 総, 通勤, 非課税, 社保, 源泉, 扶養);
        cmd.ExecuteNonQuery();
    }

    private void InsertShouyo(string num, int 回, int 総, int 社保, int 源泉, int 扶養)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """INSERT INTO "T_賞与トラン" VALUES ($n,$k,$s,$si,$g,$f);""";
        cmd.Parameters.AddWithValue("$n", num); cmd.Parameters.AddWithValue("$k", 回);
        cmd.Parameters.AddWithValue("$s", 総); cmd.Parameters.AddWithValue("$si", 社保);
        cmd.Parameters.AddWithValue("$g", 源泉); cmd.Parameters.AddWithValue("$f", 扶養);
        cmd.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand cmd, string num, int 回, int 総, int 通勤, int 非課税, int 社保, int 源泉, int 扶養)
    {
        cmd.Parameters.AddWithValue("$n", num); cmd.Parameters.AddWithValue("$k", 回);
        cmd.Parameters.AddWithValue("$s", 総); cmd.Parameters.AddWithValue("$t", 通勤);
        cmd.Parameters.AddWithValue("$h", 非課税); cmd.Parameters.AddWithValue("$si", 社保);
        cmd.Parameters.AddWithValue("$g", 源泉); cmd.Parameters.AddWithValue("$f", 扶養);
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}
