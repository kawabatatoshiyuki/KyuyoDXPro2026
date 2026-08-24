using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Bonus;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 賞与トラン（T_賞与トラン）の保存/読込を検証する。主キー＝Number＋回。
/// 支給16・控除9・各社保列・還付/徴収・備考・扶養親族数が往復一致することを確認する。Windows専用。
/// </summary>
public class BonusTranRepositoryTests : IDisposable
{
    private readonly string _dir;

    public BonusTranRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_bonustran_{Guid.NewGuid():N}");
    }

    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    private static BonusEntry Sample(string number, int 回)
    {
        var e = new BonusEntry { Number = number, 回 = 回, 賞与 = 500_000 };
        for (int i = 0; i < 16; i++) e.賞与支給[i] = (i + 1) * 100;       // 支給16の各列を区別できる値
        for (int i = 0; i < 9; i++) e.控除項目[i] = (i + 1) * 10;          // 控除9
        e.総支給額 = 513_600;
        e.健康保険 = 26_350; e.介護保険 = 4_700; e.子育支援 = 1_850;
        e.厚生年金 = 45_750; e.年金基金 = 2_500; e.小規模共済 = 1_000; e.雇用保険 = 3_080;
        e.社会保険合計 = 85_230; e.課税対象額 = 428_370; e.所得税 = 26_200;
        e.総控除額 = 111_880; e.差引支給額 = 401_720;
        e.データ個数 = 30; e.扶養親族数 = 2;
        e.還付金額 = 1_234; e.徴収金額 = 567; e.備考 = "テスト備考";
        return e;
    }

    [Fact]
    public void 賞与トランが往復する()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            var repo = new BonusTranRepository(store.Connection);
            repo.EnsureSchema();
            repo.Save(Sample("5", 2));
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new BonusTranRepository(reopened.Connection);
            repo.EnsureSchema();
            var e = repo.Load("5", 2);
            Assert.NotNull(e);
            Assert.Equal("5", e!.Number);
            Assert.Equal(2, e.回);
            Assert.Equal(500_000, e.賞与);
            for (int i = 0; i < 16; i++) Assert.Equal((i + 1) * 100, e.賞与支給[i]);
            for (int i = 0; i < 9; i++) Assert.Equal((i + 1) * 10, e.控除項目[i]);
            Assert.Equal(513_600, e.総支給額);
            Assert.Equal(26_350, e.健康保険);
            Assert.Equal(4_700, e.介護保険);
            Assert.Equal(1_850, e.子育支援);
            Assert.Equal(45_750, e.厚生年金);
            Assert.Equal(2_500, e.年金基金);
            Assert.Equal(1_000, e.小規模共済);
            Assert.Equal(3_080, e.雇用保険);
            Assert.Equal(85_230, e.社会保険合計);
            Assert.Equal(428_370, e.課税対象額);
            Assert.Equal(26_200, e.所得税);
            Assert.Equal(111_880, e.総控除額);
            Assert.Equal(401_720, e.差引支給額);
            Assert.Equal(30, e.データ個数);
            Assert.Equal(2, e.扶養親族数);
            Assert.Equal(1_234, e.還付金額);
            Assert.Equal(567, e.徴収金額);
            Assert.Equal("テスト備考", e.備考);
        }
    }

    [Fact]
    public void 主キーはNumberと回の複合_UPSERTで置換される()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new BonusTranRepository(store.Connection);
        repo.EnsureSchema();

        // 同一Number・別回は別行として共存する。
        repo.Save(Sample("5", 1));
        repo.Save(Sample("5", 2));
        Assert.Equal(2, repo.ListByRound(1).Count + repo.ListByRound(2).Count);

        // 同一(Number,回)の再保存は置換（重複行を作らない）。
        var 更新 = Sample("5", 1);
        更新.賞与 = 700_000;
        repo.Save(更新);
        var list1 = repo.ListByRound(1);
        Assert.Single(list1);
        Assert.Equal(700_000, list1[0].賞与);
    }

    [Fact]
    public void ListEarlierTotalsは当回より前の総支給を回順で返す()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new BonusTranRepository(store.Connection);
        repo.EnsureSchema();

        var e1 = Sample("5", 1); e1.総支給額 = 400_000; repo.Save(e1);
        var e2 = Sample("5", 2); e2.総支給額 = 500_000; repo.Save(e2);
        var e3 = Sample("5", 3); e3.総支給額 = 600_000; repo.Save(e3);

        Assert.Equal(new[] { 400_000, 500_000 }, repo.ListEarlierTotals("5", 3));
        Assert.Empty(repo.ListEarlierTotals("5", 1));
    }

    [Fact]
    public void Deleteで該当回のみ削除される()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new BonusTranRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(Sample("5", 1));
        repo.Save(Sample("5", 2));

        repo.Delete("5", 1);
        Assert.Null(repo.Load("5", 1));
        Assert.NotNull(repo.Load("5", 2));
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
