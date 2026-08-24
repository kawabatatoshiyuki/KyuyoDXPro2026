using System.Text;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 保存時暗号化（SQLite→SQLCipher）移行PoC：
/// レガシー暗号化DBを復号→SQLCipher暗号化DBへ移行し、正しい鍵でのみ読めることを確認する。
/// </summary>
public class SqlCipherMigrationTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private const string Key = "PayCalcDXPro-test-key-2026";
    private readonly string _target;
    private readonly bool _available;

    public SqlCipherMigrationTests()
    {
        _available = File.Exists(LegacyDbPath);
        _target = Path.Combine(Path.GetTempPath(), $"PayCalc_enc_{Guid.NewGuid():N}.db");
        if (_available)
        {
            LegacySqlCipherMigrator.Migrate(LegacyDbPath, _target, Key);
        }
    }

    [SkippableFact]
    public void 移行後のDBファイルは平文SQLiteではない()
    {
        Skip.IfNot(_available, "現行DBが見つからないためスキップ");

        Assert.True(File.Exists(_target));
        byte[] head = new byte[16];
        using (var fs = File.OpenRead(_target)) { fs.Read(head, 0, 16); }
        string magic = Encoding.ASCII.GetString(head);
        Assert.DoesNotContain("SQLite format 3", magic); // 暗号化済みなのでヘッダが平文でない
    }

    [SkippableFact]
    public void 正しい鍵で開くと別表が読める()
    {
        Skip.IfNot(_available, "現行DBが見つからないためスキップ");

        using SqliteConnection conn = LegacySqlCipherMigrator.OpenEncrypted(_target, Key);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from T_月額表別表4";
        long count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(7, count); // 月額表別表4 は7区分

        // 移行先のマスタからドメイン読込も成功する
        var repo = new LegacyMasterRepository(conn);
        Assert.Equal(7, repo.LoadWithholdingTaxTables().税率速算.Count);
    }

    [SkippableFact]
    public void 誤った鍵では開けない()
    {
        Skip.IfNot(_available, "現行DBが見つからないためスキップ");

        Assert.ThrowsAny<SqliteException>(() =>
        {
            using SqliteConnection conn = LegacySqlCipherMigrator.OpenEncrypted(_target, "wrong-key");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "select count(*) from T_月額表別表4";
            cmd.ExecuteScalar();
        });
    }

    public void Dispose()
    {
        if (File.Exists(_target))
        {
            try { File.Delete(_target); } catch { /* 一時ファイル削除失敗は無視 */ }
        }
    }
}
