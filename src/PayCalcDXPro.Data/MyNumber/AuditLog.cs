using Microsoft.Data.Sqlite;
using PayCalcDXPro.Security;

namespace PayCalcDXPro.Data.MyNumber;

/// <summary>操作ログ（現行 T_操作ログ：日時/アカウントID/操作者/処理/取扱/詳細）。改ざん検知や監査のため必ず記録する。</summary>
public sealed class AuditLog
{
    private readonly SqliteConnection _conn;

    public AuditLog(SqliteConnection conn) => _conn = conn;

    public void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS T_操作ログ (" +
            "日時 TEXT, アカウントID TEXT, 操作者 TEXT, 処理 TEXT, 取扱 TEXT, 詳細 TEXT)";
        cmd.ExecuteNonQuery();
    }

    /// <summary>ログ1件を記録する。</summary>
    public void Put(Principal op, string 処理, string 取扱, string 詳細)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO T_操作ログ (日時, アカウントID, 操作者, 処理, 取扱, 詳細) " +
            "VALUES (@d, @id, @name, @proc, @act, @detail)";
        cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff"));
        cmd.Parameters.AddWithValue("@id", op.Id);
        cmd.Parameters.AddWithValue("@name", op.Name);
        cmd.Parameters.AddWithValue("@proc", 処理);
        cmd.Parameters.AddWithValue("@act", 取扱);
        cmd.Parameters.AddWithValue("@detail", 詳細);
        cmd.ExecuteNonQuery();
    }

    /// <summary>記録件数（検証用）。</summary>
    public long Count()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM T_操作ログ";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>操作ログを新しい順に読み出す（現行 AccessLog.getAll：日時 DESC）。</summary>
    public IReadOnlyList<AuditLogRow> List(int limit = 5000)
    {
        var list = new List<AuditLogRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT 日時, アカウントID, 操作者, 処理, 取扱, 詳細 FROM T_操作ログ ORDER BY 日時 DESC LIMIT @lim";
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        while (r.Read())
            list.Add(new AuditLogRow(S(0), S(1), S(2), S(3), S(4), S(5)));
        return list;
    }
}

/// <summary>操作ログ1行（現行 T_操作ログ）。</summary>
public sealed record AuditLogRow(string 日時, string アカウントID, string 操作者, string 処理, string 取扱, string 詳細);
