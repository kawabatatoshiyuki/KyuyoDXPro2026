using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.PayItem;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>支給・控除項目設定（M_支給控除項目設定）の保存/読込と固定項目ラベルの保全を検証。Windows専用。</summary>
public class PayItemRepositoryTests : IDisposable
{
    private readonly string _dir;

    public PayItemRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_payitem_{Guid.NewGuid():N}");
    }

    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    [Fact]
    public void 既定行が作られ固定ラベルが入る()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = Open();
        var repo = new PayItemRepository(store.Connection);
        repo.EnsureSchema();
        var s = repo.Load();
        Assert.Equal(12, s.給与手当.Count);
        Assert.Equal(9, s.給与控除.Count);
        Assert.Equal(16, s.賞与手当.Count);
        Assert.Equal(9, s.賞与控除.Count);
        Assert.Equal("基本給", s.固定ラベル["基本給"]);
        Assert.Equal("社会保険合計", s.固定ラベル["給与社会保険合計"]);
    }

    [Fact]
    public void 手当_控除_非課税_印字フラグが往復する()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            var repo = new PayItemRepository(store.Connection);
            repo.EnsureSchema();
            var s = repo.Load();
            s.給与手当[0].名称 = "役職手当"; s.給与手当[0].非課税 = false;
            s.給与手当[1].名称 = "在宅手当"; s.給与手当[1].非課税 = true;
            s.給与控除[0].名称 = "親睦会費";
            s.賞与手当[0].名称 = "業績賞与加算";
            s.賞与控除[0].名称 = "賞与控除A";
            s.項目名印字 = true;
            repo.Save(s);
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new PayItemRepository(reopened.Connection);
            repo.EnsureSchema();
            var s = repo.Load();
            Assert.Equal("役職手当", s.給与手当[0].名称);
            Assert.False(s.給与手当[0].非課税);
            Assert.Equal("在宅手当", s.給与手当[1].名称);
            Assert.True(s.給与手当[1].非課税);
            Assert.Equal("親睦会費", s.給与控除[0].名称);
            Assert.Equal("業績賞与加算", s.賞与手当[0].名称);
            Assert.Equal("賞与控除A", s.賞与控除[0].名称);
            Assert.True(s.項目名印字);
            // 固定ラベルは保全。
            Assert.Equal("基本給", s.固定ラベル["基本給"]);
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
