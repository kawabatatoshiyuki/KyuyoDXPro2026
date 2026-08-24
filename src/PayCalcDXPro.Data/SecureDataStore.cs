using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Accounts;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Data.MyNumber;
using PayCalcDXPro.Security;

namespace PayCalcDXPro.Data;

/// <summary>
/// 本番の合成点（コンポジションルート）。多層防御で実証済みの各部品を、実際の鍵管理で1つに束ねる。
///
///   1. DPAPI で2つの鍵を「無ければ生成・あれば復元」して保護保存する。
///      ・DB鍵      … SQLCipher による保存時暗号化（DBファイル全体）の鍵
///      ・列暗号鍵  … マイナンバー列を AES-256-GCM で二重暗号化する <see cref="MyNumberProtector"/> の鍵
///   2. DB鍵で SQLCipher 暗号化DBへ接続する。
///   3. <see cref="MyNumberRepository"/> / <see cref="SqlCipherUserStore"/> / <see cref="AuditLog"/> を
///      組み立て、スキーマを用意して提供する。
///
/// 鍵はコード/設定に平文で持たず DPAPI（既定 LocalMachine）に紐付くため、同一マシンで再起動しても
/// 同じ鍵で開け直せる。鍵ファイルを別マシンへ持ち出しても DPAPI で復元できないので無効化される。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecureDataStore : IDisposable
{
    private const string DbFileName = "PayCalcDXPro.db";
    private const string DbKeyFileName = "db.key";
    private const string ColumnKeyFileName = "mynumber.key";

    private readonly SqliteConnection _conn;

    /// <summary>暗号化DBへの接続（必要に応じて直接クエリする用途向け）。</summary>
    public SqliteConnection Connection => _conn;

    /// <summary>マイナンバーの保存・参照（列暗号化＋マスキング＋監査）。</summary>
    public MyNumberRepository MyNumber { get; }

    /// <summary>利用者アカウント（ハッシュ＋ロール）。</summary>
    public SqlCipherUserStore Users { get; }

    /// <summary>操作ログ。</summary>
    public AuditLog Audit { get; }

    /// <param name="dataDirectory">
    /// データ格納フォルダ。直下に暗号化DB（<c>PayCalcDXPro.db</c>）、
    /// <c>keys\</c> 配下に DPAPI 保護した鍵ファイル2種を置く。
    /// </param>
    /// <param name="scope">DPAPI の保護範囲。常駐サービス想定の既定は <see cref="DataProtectionScope.LocalMachine"/>。</param>
    public SecureDataStore(string dataDirectory,
        DataProtectionScope scope = DataProtectionScope.LocalMachine)
    {
        Directory.CreateDirectory(dataDirectory);
        string keysDir = Path.Combine(dataDirectory, "keys");

        // 1) DPAPI で鍵を load-or-create（初回は生成・以降は同じ鍵を復元）
        byte[] dbKey = DpapiKeyProtector.LoadOrCreate(Path.Combine(keysDir, DbKeyFileName), 32, scope);
        byte[] columnKey = DpapiKeyProtector.LoadOrCreate(Path.Combine(keysDir, ColumnKeyFileName), 32, scope);

        // 2) DB鍵で SQLCipher 暗号化DBへ接続（新規ファイルも鍵付きで開けば暗号化生成される）
        _conn = OpenEncrypted(Path.Combine(dataDirectory, DbFileName), dbKey);

        // 3) 列暗号化と各リポジトリを組み立て、スキーマを用意
        var protector = new MyNumberProtector(columnKey);
        Audit = new AuditLog(_conn);
        MyNumber = new MyNumberRepository(_conn, protector, Audit);
        Users = new SqlCipherUserStore(_conn);

        MyNumber.EnsureSchema(); // 監査ログのスキーマも内部で用意される
        Users.EnsureSchema();
    }

    /// <summary>暗号化DBが既に存在するか（移行済み・初回判定に使用）。</summary>
    public static bool Exists(string dataDirectory) => File.Exists(Path.Combine(dataDirectory, DbFileName));

    /// <summary>
    /// 現行の暗号化SQLite（AES-128-CBC）を、このストアが開ける SQLCipher 暗号化DBへ移行する。
    /// 鍵は DPAPI で生成・保護し、以降は <see cref="SecureDataStore"/> がそのまま開ける。
    /// </summary>
    /// <param name="dataDirectory">移行先データフォルダ（<c>PayCalcDXPro.db</c> と <c>keys\</c> を作成）。</param>
    /// <param name="legacyEncryptedPath">現行の暗号化DBファイル。</param>
    public static void MigrateLegacyInto(
        string dataDirectory,
        string legacyEncryptedPath,
        DataProtectionScope scope = DataProtectionScope.LocalMachine,
        string legacyPassword = "SyuhouSystem")
    {
        Directory.CreateDirectory(dataDirectory);
        string keysDir = Path.Combine(dataDirectory, "keys");

        // DB鍵・列暗号鍵を load-or-create（移行後に SecureDataStore が同じ鍵で開ける）。
        byte[] dbKey = DpapiKeyProtector.LoadOrCreate(Path.Combine(keysDir, DbKeyFileName), 32, scope);
        DpapiKeyProtector.LoadOrCreate(Path.Combine(keysDir, ColumnKeyFileName), 32, scope);

        // 現行DB → SQLCipher暗号化DB（パスフレーズは OpenEncrypted と同一の Base64(dbKey)）。
        LegacySqlCipherMigrator.Migrate(
            legacyEncryptedPath,
            Path.Combine(dataDirectory, DbFileName),
            Convert.ToBase64String(dbKey),
            legacyPassword);
    }

    /// <summary>DPAPI 由来のバイト鍵を SQLCipher のパスフレーズに変換して接続を開く。</summary>
    private static SqliteConnection OpenEncrypted(string dbPath, byte[] key)
    {
        // SQLCipher の鍵は文字列パスフレーズ。Base64 化し、接続文字列ビルダで安全にエスケープする。
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Password = Convert.ToBase64String(key),
        };
        var conn = new SqliteConnection(csb.ConnectionString);
        conn.Open();
        return conn;
    }

    public void Dispose() => _conn.Dispose();
}
