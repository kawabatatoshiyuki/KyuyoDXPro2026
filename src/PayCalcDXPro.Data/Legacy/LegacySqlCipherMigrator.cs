using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.Legacy;

/// <summary>
/// 現行の暗号化SQLite（AES-128-CBCのファイル暗号化）を、SQLCipher による保存時暗号化DBへ移行する。
/// 経路：レガシー復号 → 平文SQLite → sqlcipher_export で暗号化DBへ丸ごとコピー。
/// これにより「サーバ常駐でも常に暗号化されたまま読み書き」できる状態へ移す（提案の SQLCipher 方針）。
/// </summary>
public static class LegacySqlCipherMigrator
{
    /// <summary>暗号化レガシーDB → SQLCipher暗号化DB へ移行する。</summary>
    /// <param name="legacyEncryptedPath">現行の暗号化DBファイル。</param>
    /// <param name="targetEncryptedPath">出力する SQLCipher 暗号化DBファイル。</param>
    /// <param name="key">SQLCipher の鍵（本番では DPAPI 等で保護した値を渡す）。</param>
    /// <param name="legacyPassword">現行のファイル暗号化パスワード（既定 "SyuhouSystem"）。</param>
    public static void Migrate(
        string legacyEncryptedPath,
        string targetEncryptedPath,
        string key,
        string legacyPassword = "SyuhouSystem")
    {
        string plainTmp = Path.Combine(Path.GetTempPath(), $"PayCalc_migrate_{Guid.NewGuid():N}.db");
        try
        {
            // 1) レガシー復号 → 平文SQLite
            LegacyDbDecryptor.DecryptFile(legacyEncryptedPath, plainTmp, legacyPassword);

            if (File.Exists(targetEncryptedPath)) File.Delete(targetEncryptedPath);

            // 2) 平文を開き、暗号化DBへ sqlcipher_export で丸ごとコピー
            using var src = new SqliteConnection($"Data Source={plainTmp}");
            src.Open();

            using var cmd = src.CreateCommand();
            cmd.CommandText =
                "ATTACH DATABASE @target AS enc KEY @key;" +
                "SELECT sqlcipher_export('enc');" +
                "DETACH DATABASE enc;";
            cmd.Parameters.AddWithValue("@target", targetEncryptedPath);
            cmd.Parameters.AddWithValue("@key", key);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            if (File.Exists(plainTmp))
            {
                try { File.Delete(plainTmp); } catch { /* 一時ファイル削除失敗は無視 */ }
            }
        }
    }

    /// <summary>SQLCipher 暗号化DBへの接続を開く（鍵を PRAGMA key で適用）。</summary>
    public static SqliteConnection OpenEncrypted(string encryptedPath, string key)
    {
        var conn = new SqliteConnection($"Data Source={encryptedPath};Password={key}");
        conn.Open();
        return conn;
    }
}
