using Microsoft.Data.Sqlite;
using PayCalcDXPro.Security;

namespace PayCalcDXPro.Data.Accounts;

/// <summary>
/// SQLCipher 上の利用者アカウントストア（現行 M_セキュリティアカウント設定 にハッシュ＋ロール列を追加した形）。
/// パスワードは <see cref="PasswordHasher"/> でハッシュ化して保存する。
/// </summary>
public sealed class SqlCipherUserStore : IUserStore
{
    private readonly SqliteConnection _conn;

    public SqlCipherUserStore(SqliteConnection conn) => _conn = conn;

    public void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS M_セキュリティアカウント設定 (" +
            "アカウントID TEXT PRIMARY KEY, 利用者 TEXT, パスワードハッシュ TEXT, ロール TEXT)";
        cmd.ExecuteNonQuery();
    }

    /// <summary>アカウントを作成/更新（パスワードはハッシュ化して保存）。</summary>
    public void CreateOrUpdate(string id, string name, string password, params Role[] roles)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO M_セキュリティアカウント設定 (アカウントID, 利用者, パスワードハッシュ, ロール) " +
            "VALUES (@id, @name, @hash, @roles) " +
            "ON CONFLICT(アカウントID) DO UPDATE SET 利用者=@name, パスワードハッシュ=@hash, ロール=@roles";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@hash", PasswordHasher.Hash(password));
        cmd.Parameters.AddWithValue("@roles", string.Join(",", roles));
        cmd.ExecuteNonQuery();
    }

    public UserAccount? FindById(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT 利用者, パスワードハッシュ, ロール FROM M_セキュリティアカウント設定 WHERE アカウントID=@id";
        cmd.Parameters.AddWithValue("@id", id);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new UserAccount
        {
            Id = id,
            Name = r.IsDBNull(0) ? "" : r.GetString(0),
            PasswordHash = r.IsDBNull(1) ? "" : r.GetString(1),
            Roles = ParseRoles(r.IsDBNull(2) ? "" : r.GetString(2)),
        };
    }

    public long Count()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM M_セキュリティアカウント設定";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>全アカウントを削除（現行の単一アカウント運用における「解除」／設定前クリア）。</summary>
    public void DeleteAll()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM M_セキュリティアカウント設定";
        cmd.ExecuteNonQuery();
    }

    /// <summary>登録されている単一アカウントの ID と利用者名を返す（未登録なら null）。パスワードはハッシュのため返さない。</summary>
    public (string Id, string Name)? GetSingle()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT アカウントID, 利用者 FROM M_セキュリティアカウント設定 LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1));
    }

    private static IReadOnlySet<Role> ParseRoles(string csv)
    {
        var set = new HashSet<Role>();
        foreach (var s in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<Role>(s, out var role)) set.Add(role);
        }
        return set;
    }
}
