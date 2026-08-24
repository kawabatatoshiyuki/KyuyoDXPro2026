using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Accounts;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Security;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>SQLCipher 上の利用者アカウントストアと認証の突合。</summary>
public class SqlCipherUserStoreTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnection _conn;
    private readonly SqlCipherUserStore _store;

    public SqlCipherUserStoreTests()
    {
        _db = Path.Combine(Path.GetTempPath(), $"PayCalc_users_{Guid.NewGuid():N}.db");
        _conn = LegacySqlCipherMigrator.OpenEncrypted(_db, "user-db-key");
        _store = new SqlCipherUserStore(_conn);
        _store.EnsureSchema();
        _store.CreateOrUpdate("admin", "管理者太郎", "Admin#123", Role.管理者);
        _store.CreateOrUpdate("pay", "給与花子", "Pay#123", Role.給与担当);
    }

    [Fact]
    public void パスワードはハッシュで保存され平文は残らない()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT パスワードハッシュ FROM M_セキュリティアカウント設定 WHERE アカウントID='admin'";
        string hash = (string)cmd.ExecuteScalar()!;
        Assert.StartsWith("pbkdf2$", hash);
        Assert.DoesNotContain("Admin#123", hash);
    }

    [Fact]
    public void 認証は正資格でPrincipalとロールを返す()
    {
        var auth = new AuthenticationService(_store);

        Principal? admin = auth.Authenticate("admin", "Admin#123");
        Assert.NotNull(admin);
        Assert.True(admin!.IsInRole(Role.管理者));
        Assert.True(admin.CanDecryptMyNumber);

        Principal? pay = auth.Authenticate("pay", "Pay#123");
        Assert.NotNull(pay);
        Assert.True(pay!.IsInRole(Role.給与担当));
        Assert.False(pay.CanDecryptMyNumber);

        Assert.Null(auth.Authenticate("admin", "wrong"));
        Assert.Null(auth.Authenticate("ghost", "x"));
    }

    public void Dispose()
    {
        _conn.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_db))
        {
            try { File.Delete(_db); } catch { /* 一時ファイル削除失敗は無視 */ }
        }
    }
}
