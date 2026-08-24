using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.MyNumber;
using PayCalcDXPro.Security;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 本適用の統合：DPAPI 永続鍵で開いた SQLCipher 暗号化DB上で、マイナンバー列暗号化・認証・監査が
/// 一貫して動作し、Dispose 後に同一フォルダで開き直しても（＝アプリ再起動相当）同じ鍵・同じデータで
/// 復号できることを確認する。DPAPI は Windows 専用のため非 Windows では各テストをスキップする。
/// </summary>
public class SecureDataStoreTests : IDisposable
{
    private readonly string _dir;

    private static readonly Principal Admin = Principal.Of("admin01", "管理者太郎", Role.管理者);
    private static readonly Principal 給与担当 = Principal.Of("pay01", "給与花子", Role.給与担当);

    public SecureDataStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_secure_{Guid.NewGuid():N}");
    }

    // テストはマシン共有領域へ書かないよう CurrentUser スコープで開く。呼び出し側で Windows をガード済み。
    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    [Fact]
    public void 初回オープンで暗号化DBとDPAPI保護鍵ファイルが生成される()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows 専用

        using (Open()) { }

        Assert.True(File.Exists(Path.Combine(_dir, "PayCalcDXPro.db")));
        Assert.True(File.Exists(Path.Combine(_dir, "keys", "db.key")));
        Assert.True(File.Exists(Path.Combine(_dir, "keys", "mynumber.key")));
    }

    [Fact]
    public void 再オープンしても同じ鍵で同じマイナンバーを復号できる()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            store.MyNumber.Save(new MyNumberRecord
            {
                Number = "0001",
                本人 = "123456789012",
                扶養親族 = new string?[] { "111122223333", null, null, null, null, null, null },
            }, Admin);
        }
        SqliteConnection.ClearAllPools(); // 接続プールを解放してファイルを開き直す

        // アプリ再起動相当：同一フォルダで開き直す（鍵は DPAPI から復元される）
        using var reopened = Open();
        MyNumberRecord r = reopened.MyNumber.GetDecrypted("0001", Admin);
        Assert.Equal("123456789012", r.本人);
        Assert.Equal("111122223333", r.扶養親族[0]);
    }

    [Fact]
    public void DBファイルは平文SQLiteではなくマイナンバー列も平文で残らない()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            store.MyNumber.Save(new MyNumberRecord { Number = "0001", 本人 = "123456789012" }, Admin);
        }
        SqliteConnection.ClearAllPools();

        // (1) ファイル先頭が平文 SQLite ヘッダでない＝SQLCipher 透過暗号化が効いている
        byte[] head = new byte[16];
        using (var fs = File.OpenRead(Path.Combine(_dir, "PayCalcDXPro.db"))) { fs.Read(head, 0, 16); }
        Assert.DoesNotContain("SQLite format 3", Encoding.ASCII.GetString(head));

        // (2) 鍵付きで開いても列の生値は平文でない＝列暗号化が二重に効いている
        using var store2 = Open();
        using var cmd = store2.Connection.CreateCommand();
        cmd.CommandText = "SELECT 本人 FROM T_マイナンバー情報 WHERE Number='0001'";
        string stored = (string)cmd.ExecuteScalar()!;
        Assert.DoesNotContain("123456789012", stored);
    }

    [Fact]
    public void 認証と認可が再オープン後も一貫して通る()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            store.Users.CreateOrUpdate("admin", "管理者太郎", "Admin#123", Role.管理者);
            store.Users.CreateOrUpdate("pay", "給与花子", "Pay#123", Role.給与担当);
        }
        SqliteConnection.ClearAllPools();

        using var reopened = Open();
        var auth = new AuthenticationService(reopened.Users);

        Principal? admin = auth.Authenticate("admin", "Admin#123");
        Assert.NotNull(admin);
        Assert.True(admin!.CanDecryptMyNumber);
        Assert.Null(auth.Authenticate("pay", "wrong"));
    }

    [Fact]
    public void 復号権限が無い操作は拒否され監査ログが残る()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var store = Open();
        var mynumber = store.MyNumber; // Windows ガード内でプロパティ取得（ラムダへ持ち込まない）
        mynumber.Save(new MyNumberRecord { Number = "0001", 本人 = "123456789012" }, Admin);

        long before = store.Audit.Count();
        Assert.Throws<UnauthorizedAccessException>(() => mynumber.GetDecrypted("0001", 給与担当));
        Assert.Equal(before + 1, store.Audit.Count());
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
