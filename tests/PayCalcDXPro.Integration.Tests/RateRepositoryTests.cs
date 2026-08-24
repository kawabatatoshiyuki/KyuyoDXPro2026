using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Rate;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>保険料率（T_料率）の保存/読込を検証する。Windows専用。</summary>
public class RateRepositoryTests : IDisposable
{
    private readonly string _dir;

    public RateRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_rate_{Guid.NewGuid():N}");
    }

    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    [Fact]
    public void 料率が往復する()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            var repo = new RateRepository(store.Connection);
            repo.EnsureSchema();
            var s = repo.Load(); // 既定行
            s.健康保険一般 = 10.0; s.健康保険本人 = 5.0;
            s.厚生年金一般 = 18.3; s.厚生年金本人 = 9.15;
            s.介護保険一般 = 1.82; s.介護保険本人 = 0.91;
            s.雇用保険一般 = 6; s.雇用保険一般以外 = 7;
            repo.Save(s);
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new RateRepository(reopened.Connection);
            repo.EnsureSchema();
            var s = repo.Load();
            Assert.Equal(10.0, s.健康保険一般);
            Assert.Equal(5.0, s.健康保険本人);
            Assert.Equal(18.3, s.厚生年金一般!.Value, 3);
            Assert.Equal(0.91, s.介護保険本人!.Value, 3);
            Assert.Equal(6, s.雇用保険一般);
            Assert.Equal(7, s.雇用保険一般以外);
        }
    }

    [Fact]
    public void 保険料額表が往復する()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            var repo = new RateRepository(store.Connection);
            repo.EnsureSchema();
            // シード行を消してから2行だけ保存。
            using (var del = store.Connection.CreateCommand())
            {
                del.CommandText = "DELETE FROM T_料率保険料額表";
                del.ExecuteNonQuery();
            }
            repo.SaveTable(new List<RateTableRow>
            {
                new() { No = 1, 等級短期 = 1, 報酬月額至 = 63000, 標準報酬月額短期 = 58000, 健康保険 = 2900, 厚生年金 = 8052 },
                new() { No = 2, 等級短期 = 2, 報酬月額自 = 63000, 報酬月額至 = 73000, 標準報酬月額短期 = 68000, 健康保険 = 3400 },
            });
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new RateRepository(reopened.Connection);
            // 再EnsureSchemaでは既にデータがあるので再シードされない。
            repo.EnsureSchema();
            var t = repo.LoadTable();
            Assert.Equal(2, t.Count);
            Assert.Equal(58000, t[0].標準報酬月額短期);
            Assert.Equal(2900, t[0].健康保険);
            Assert.Equal(73000, t[1].報酬月額至);
            Assert.Equal(3400, t[1].健康保険);
        }
    }

    [Fact]
    public void 空DBでは初期データがシードされる()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new RateRepository(store.Connection);
        repo.EnsureSchema();

        var rates = repo.Load();
        Assert.Equal(10, rates.健康保険一般);
        Assert.Equal(9.15, rates.厚生年金本人!.Value, 3);

        var table = repo.LoadTable();
        Assert.Equal(50, table.Count);
        Assert.Equal(58000, table[0].標準報酬月額短期);
        Assert.Equal(2900, table[0].健康保険);
        Assert.Equal(69500, table[49].健康保険);
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
