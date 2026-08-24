using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Legacy;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// データ移行の「現行保証」：現行暗号化DB→SQLCipher暗号化DBへ移行し、移行前後で
/// テーブル行数と計算マスタが完全一致することを確認する（Windows＋現行DBがある環境でのみ実行）。
/// </summary>
public class MigrationFidelityTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"PayCalc_mig_{Guid.NewGuid():N}");
    private string? _legacyTmp;

    [SkippableFact]
    public void 移行後はテーブル行数が現行と一致する()
    {
        if (!OperatingSystem.IsWindows()) return; // 解析器が認識するOSガード
        Skip.IfNot(File.Exists(LegacyDbPath), "現行DBが見つからないためスキップ");

        SecureDataStore.MigrateLegacyInto(_dataDir, LegacyDbPath, DataProtectionScope.CurrentUser);

        using SqliteConnection legacy = OpenLegacy();
        using var store = new SecureDataStore(_dataDir, DataProtectionScope.CurrentUser);

        int mismatches = 0;
        foreach (string t in TableNames(legacy))
            if (Count(legacy, t) != Count(store.Connection, t)) mismatches++;

        Assert.Equal(0, mismatches);
    }

    [SkippableFact]
    public void 移行後も計算マスタが現行と完全一致する()
    {
        if (!OperatingSystem.IsWindows()) return; // 解析器が認識するOSガード
        Skip.IfNot(File.Exists(LegacyDbPath), "現行DBが見つからないためスキップ");

        SecureDataStore.MigrateLegacyInto(_dataDir, LegacyDbPath, DataProtectionScope.CurrentUser);

        using SqliteConnection legacy = OpenLegacy();
        using var store = new SecureDataStore(_dataDir, DataProtectionScope.CurrentUser);

        var repoL = new LegacyMasterRepository(legacy);
        var repoM = new LegacyMasterRepository(store.Connection);

        Assert.Equal(J(repoL.LoadWithholdingTaxTables()), J(repoM.LoadWithholdingTaxTables()));
        Assert.Equal(J(repoL.LoadSocialInsuranceTable()), J(repoM.LoadSocialInsuranceTable()));
        Assert.Equal(J(repoL.LoadYearEndTables()), J(repoM.LoadYearEndTables()));
        Assert.Equal(J(repoL.LoadPensionConversionTable()), J(repoM.LoadPensionConversionTable()));
    }

    private SqliteConnection OpenLegacy()
    {
        _legacyTmp = Path.Combine(Path.GetTempPath(), $"PayCalc_migverify_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _legacyTmp);
        var c = new SqliteConnection($"Data Source={_legacyTmp}");
        c.Open();
        return c;
    }

    private static string J(object o) => JsonSerializer.Serialize(o);

    private static List<string> TableNames(SqliteConnection c)
    {
        var list = new List<string>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "select name from sqlite_master where type='table' and name not like 'sqlite_%' order by name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static long Count(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"select count(*) from \"{table}\"";
        try { return (long)cmd.ExecuteScalar()!; } catch { return -1; }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string? p in new[] { _legacyTmp })
            if (p is not null && File.Exists(p)) { try { File.Delete(p); } catch { /* 無視 */ } }
        if (Directory.Exists(_dataDir)) { try { Directory.Delete(_dataDir, true); } catch { /* 無視 */ } }
    }
}
