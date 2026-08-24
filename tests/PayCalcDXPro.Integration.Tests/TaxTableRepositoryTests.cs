using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.TaxTable;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>所得税（月額表）系テーブルの保存/読込を検証する。Windows専用。</summary>
public class TaxTableRepositoryTests : IDisposable
{
    private readonly string _dir;

    public TaxTableRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_taxtable_{Guid.NewGuid():N}");
    }

    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    [Fact]
    public void 空DBでは現行DB由来の初期データがシードされる()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var repo = new TaxTableRepository(store.Connection);
        repo.EnsureSchema();

        var b = repo.Load();
        Assert.Equal(5, b.別表1.Count);
        Assert.Single(b.別表2);
        Assert.Equal(5, b.別表3.Count);
        Assert.Equal(7, b.別表4.Count);
        Assert.Equal(20, b.賞与甲.Count);
        Assert.Equal(5, b.賞与乙.Count);

        // 代表値の確認。
        Assert.Equal(54167, b.別表1[0].控除額);
        Assert.Null(b.別表1[0].給与額自);
        Assert.Equal(30, b.別表1[1].控除率);
        Assert.Equal(31667, b.別表2[0].配偶者控除);
        Assert.Equal(48334, b.別表3[0].控除額);
        Assert.Equal(45.945, b.別表4[6].税率!.Value, 3);
        Assert.Equal(82000, b.賞与甲[0].金額0人);
        Assert.Equal(2.042, b.賞与甲[0].率!.Value, 3);
        Assert.Equal(38.798, b.賞与乙[3].率!.Value, 3);
        Assert.Null(b.賞与乙[0].金額);
    }

    [Fact]
    public void 全テーブルが往復する()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            var repo = new TaxTableRepository(store.Connection);
            repo.EnsureSchema();
            var b = repo.Load();
            // 値を変更して保存。
            b.別表1[0].控除額 = 50000;
            b.別表4[0].税率 = 5.5;
            b.賞与甲[0].金額7人以上 = 999000;
            b.賞与乙[1].金額 = 222222;
            repo.Save(b);
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new TaxTableRepository(reopened.Connection);
            repo.EnsureSchema(); // 既にデータがあるので再シードされない。
            var b = repo.Load();
            Assert.Equal(50000, b.別表1[0].控除額);
            Assert.Equal(5.5, b.別表4[0].税率!.Value, 3);
            Assert.Equal(999000, b.賞与甲[0].金額7人以上);
            Assert.Equal(222222, b.賞与乙[1].金額);
            // 行数は維持される。
            Assert.Equal(20, b.賞与甲.Count);
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
