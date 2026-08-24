using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Data.MyNumber;
using PayCalcDXPro.Security;
using Xunit;
// Principal/Role は PayCalcDXPro.Security

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// マイナンバー列暗号化の本適用：SQLCipher暗号化DBの上で、各番号列を AES-256-GCM で二重暗号化し、
/// マスキング表示・復号権限・監査ログまで通すことを確認する。
/// </summary>
public class MyNumberRepositoryTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnection _conn;
    private readonly MyNumberRepository _repo;
    private readonly AuditLog _audit;
    private static readonly Principal Admin = Principal.Of("admin01", "管理者太郎", Role.管理者);
    private static readonly Principal 給与担当 = Principal.Of("pay01", "給与花子", Role.給与担当);

    public MyNumberRepositoryTests()
    {
        _db = Path.Combine(Path.GetTempPath(), $"PayCalc_mn_{Guid.NewGuid():N}.db");
        // SQLCipher 暗号化DBを新規作成（存在しないファイルを鍵付きで開くと暗号化DBとして生成される）
        _conn = LegacySqlCipherMigrator.OpenEncrypted(_db, "db-cipher-key");

        var protector = new MyNumberProtector(RandomNumberGenerator.GetBytes(32));
        _audit = new AuditLog(_conn);
        _repo = new MyNumberRepository(_conn, protector, _audit);
        _repo.EnsureSchema();

        _repo.Save(new MyNumberRecord
        {
            Number = "0001",
            本人 = "123456789012",
            配偶者 = "987654321098",
            扶養親族 = new string?[] { "111122223333", null, null, null, null, null, null },
        }, Admin);
    }

    [Fact]
    public void 保存された列は平文ではなく暗号化されている()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 本人 FROM T_マイナンバー情報 WHERE Number='0001'";
        string stored = (string)cmd.ExecuteScalar()!;
        Assert.NotEqual("123456789012", stored);
        Assert.DoesNotContain("123456789012", stored);
    }

    [Fact]
    public void マスキング参照は下4桁のみで監査ログを残さない()
    {
        long before = _audit.Count();
        MyNumberRecord? r = _repo.GetMasked("0001");

        Assert.NotNull(r);
        Assert.Equal("********9012", r!.本人);
        Assert.Equal("********1098", r.配偶者);
        Assert.Equal("********3333", r.扶養親族[0]);
        Assert.Null(r.扶養親族[1]);
        Assert.Equal(before, _audit.Count()); // マスキングはログを残さない
    }

    [Fact]
    public void マイナンバー閲覧ロールで全桁取得でき監査ログが残る()
    {
        long before = _audit.Count();
        MyNumberRecord r = _repo.GetDecrypted("0001", Admin);

        Assert.Equal("123456789012", r.本人);
        Assert.Equal("111122223333", r.扶養親族[0]);
        Assert.Equal(before + 1, _audit.Count()); // 参照が記録される
    }

    [Fact]
    public void 復号ロールが無いと例外かつ拒否ログが残る()
    {
        long before = _audit.Count();
        Assert.Throws<UnauthorizedAccessException>(() => _repo.GetDecrypted("0001", 給与担当));
        Assert.Equal(before + 1, _audit.Count()); // 参照拒否が記録される
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
