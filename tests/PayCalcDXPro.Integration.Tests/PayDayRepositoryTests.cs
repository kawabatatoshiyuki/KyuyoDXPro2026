using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.PayDay;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>支払日情報（M_支給日情報）の保存/読込と、処理済みフラグの保全を検証する。Windows専用。</summary>
public class PayDayRepositoryTests : IDisposable
{
    private readonly string _dir;

    public PayDayRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_payday_{Guid.NewGuid():N}");
    }

    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    [Fact]
    public void 締日_支給日_月別_賞与が往復する()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            var repo = new PayDayRepository(store.Connection);
            repo.EnsureSchema();
            var p = repo.Load(); // 既定（12月＋4賞与の空行）
            p.支給年 = 2026;
            p.締日付 = "末";
            p.支給日 = 25;
            p.月別[0].支給月 = 1; p.月別[0].支給日 = 25; p.月別[0].対象年 = 2025; p.月別[0].対象月 = 12; p.月別[0].所定勤務日数 = 21;
            p.賞与[0].支給月 = 7; p.賞与[0].支給日 = 10;
            repo.Save(p);
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new PayDayRepository(reopened.Connection);
            repo.EnsureSchema();
            var p = repo.Load();
            Assert.Equal(2026, p.支給年);
            Assert.Equal("末", p.締日付);
            Assert.Equal(25, p.支給日);
            Assert.Equal(12, p.月別.Count);
            Assert.Equal(1, p.月別[0].支給月);
            Assert.Equal(2025, p.月別[0].対象年);
            Assert.Equal(21, p.月別[0].所定勤務日数);
            Assert.Equal(4, p.賞与.Count);
            Assert.Equal(7, p.賞与[0].支給月);
            Assert.Equal(10, p.賞与[0].支給日);
        }
    }

    [Fact]
    public void 処理済みフラグは保存で保全される()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new PayDayRepository(store.Connection);
        repo.EnsureSchema();
        repo.Save(new PayDayInfo
        {
            支給年 = 2026,
            月別 = Enumerable.Range(1, 12).Select(_ => new MonthlyPayDay()).ToList(),
            賞与 = Enumerable.Range(1, 4).Select(_ => new BonusPayDay()).ToList(),
        });

        // 給与/賞与処理が処理済みフラグを立てた状態を作る。
        using (var cmd = store.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE M_支給日情報 SET \"処理済みフラグ１\"='1', \"賞与処理済みフラグ１\"='1' WHERE 会社番号=1";
            cmd.ExecuteNonQuery();
        }

        // 画面からの保存（フラグは触らない）。
        var p = repo.Load();
        Assert.Equal("1", p.月別[0].処理済みフラグ);
        Assert.Equal("1", p.賞与[0].処理済みフラグ);
        p.月別[0].支給日 = 25;
        repo.Save(p);

        using (var cmd = store.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT \"処理済みフラグ１\", \"賞与処理済みフラグ１\", \"支給日１\" FROM M_支給日情報 WHERE 会社番号=1";
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("1", r.GetString(0)); // 月別フラグ保全
            Assert.Equal("1", r.GetString(1)); // 賞与フラグ保全
            Assert.Equal(25, (int)r.GetInt64(2));
        }
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
