using Microsoft.Data.Sqlite;
using PayCalcDXPro.Security;

namespace PayCalcDXPro.Data.MyNumber;
// Principal / MyNumberProtector は PayCalcDXPro.Security 名前空間

/// <summary>
/// マイナンバーの保存・参照リポジトリ。多層防御の方針：
///  ・DB全体は SQLCipher で透過暗号化（接続側）。
///  ・本リポジトリは各番号列を <see cref="MyNumberProtector"/>（AES-256-GCM）で「列単位」二重暗号化して保存。
///  ・通常の参照はマスキング（下4桁）。全桁の復号は復号権限が必要で、必ず監査ログへ記録する。
/// </summary>
public sealed class MyNumberRepository
{
    private const string 処理名 = "マイナンバー";
    private static readonly string[] 親族列 =
        { "扶養親族１", "扶養親族２", "扶養親族３", "扶養親族４", "扶養親族５", "扶養親族６", "扶養親族７" };

    private readonly SqliteConnection _conn;
    private readonly MyNumberProtector _protector;
    private readonly AuditLog _audit;

    public MyNumberRepository(SqliteConnection conn, MyNumberProtector protector, AuditLog audit)
    {
        _conn = conn;
        _protector = protector;
        _audit = audit;
    }

    public void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS T_マイナンバー情報 (" +
            "Number TEXT PRIMARY KEY, 本人 TEXT, 配偶者 TEXT, " +
            "扶養親族１ TEXT, 扶養親族２ TEXT, 扶養親族３ TEXT, 扶養親族４ TEXT, " +
            "扶養親族５ TEXT, 扶養親族６ TEXT, 扶養親族７ TEXT)";
        cmd.ExecuteNonQuery();
        _audit.EnsureSchema();
    }

    /// <summary>マイナンバーを列暗号化して保存（登録/更新）。</summary>
    public void Save(MyNumberRecord record, Principal op)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO T_マイナンバー情報 (Number, 本人, 配偶者, 扶養親族１, 扶養親族２, 扶養親族３, 扶養親族４, 扶養親族５, 扶養親族６, 扶養親族７) " +
            "VALUES (@no, @honnin, @haigu, @f1, @f2, @f3, @f4, @f5, @f6, @f7) " +
            "ON CONFLICT(Number) DO UPDATE SET " +
            "本人=@honnin, 配偶者=@haigu, 扶養親族１=@f1, 扶養親族２=@f2, 扶養親族３=@f3, " +
            "扶養親族４=@f4, 扶養親族５=@f5, 扶養親族６=@f6, 扶養親族７=@f7";

        cmd.Parameters.AddWithValue("@no", record.Number);
        cmd.Parameters.AddWithValue("@honnin", Param(record.本人));
        cmd.Parameters.AddWithValue("@haigu", Param(record.配偶者));
        for (int i = 0; i < 7; i++)
            cmd.Parameters.AddWithValue($"@f{i + 1}", Param(i < record.扶養親族.Count ? record.扶養親族[i] : null));
        cmd.ExecuteNonQuery();

        _audit.Put(op, 処理名, "更新", $"Number:{record.Number}");
    }

    /// <summary>マスキング表示用（下4桁のみ）。復号権限は不要、監査ログも残さない。</summary>
    public MyNumberRecord? GetMasked(string number)
        => Read(number, value => Dec(value) is { } d ? MyNumberProtector.Mask(d) : null);

    /// <summary>全桁を復号して取得。マイナンバー復号ロールが必須で、必ず監査ログを残す。</summary>
    public MyNumberRecord GetDecrypted(string number, Principal op)
    {
        if (!op.CanDecryptMyNumber)
        {
            _audit.Put(op, 処理名, "参照拒否", $"Number:{number}（復号権限なし）");
            throw new UnauthorizedAccessException("マイナンバーの復号権限がありません。");
        }

        MyNumberRecord? rec = Read(number, Dec);
        if (rec is null) throw new KeyNotFoundException($"マイナンバーが見つかりません: {number}");

        _audit.Put(op, 処理名, "参照", $"Number:{number}");
        return rec;
    }

    // ---- internal ----

    private MyNumberRecord? Read(string number, Func<string?, string?> transform)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT 本人, 配偶者, 扶養親族１, 扶養親族２, 扶養親族３, 扶養親族４, 扶養親族５, 扶養親族６, 扶養親族７ " +
            "FROM T_マイナンバー情報 WHERE Number=@no";
        cmd.Parameters.AddWithValue("@no", number);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        string? Col(int i) => r.IsDBNull(i) ? null : transform(r.GetString(i));

        return new MyNumberRecord
        {
            Number = number,
            本人 = Col(0),
            配偶者 = Col(1),
            扶養親族 = Enumerable.Range(2, 7).Select(Col).ToArray(),
        };
    }

    private string? Enc(string? plain) => string.IsNullOrEmpty(plain) ? null : _protector.Encrypt(plain);

    /// <summary>暗号化値（null は DBNull）をパラメータ値へ。</summary>
    private object Param(string? plain) => (object?)Enc(plain) ?? DBNull.Value;

    private string? Dec(string? cipher) => string.IsNullOrEmpty(cipher) ? null : _protector.Decrypt(cipher);
}
