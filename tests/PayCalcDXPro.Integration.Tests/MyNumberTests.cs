using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.MyNumber;
using PayCalcDXPro.Security;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>マイナンバーの検証（チェックデジット）と、リポジトリの列暗号化往復・監査ログを検証する。</summary>
public class MyNumberTests
{
    [Theory]
    [InlineData("", true)]                       // 未入力は許容
    [InlineData("123", false)]                   // 桁数不足
    [InlineData("1234567890123", false)]         // 桁数超過
    [InlineData("12345678901a", false)]          // 数字以外
    [InlineData("000000000000", false)]          // 全ゼロ
    public void 形式チェック(string input, bool expected)
        => Assert.Equal(expected, MyNumberValidator.IsValid(input));

    [Fact]
    public void チェックデジット_本体11桁に対し有効な下1桁はちょうど1つ()
    {
        // 上位11桁を固定し、下1桁（チェックデジット）を0〜9で試すと、有効になるのは1つだけ。
        string body = "12345678901";
        var valid = Enumerable.Range(0, 10).Where(c => MyNumberValidator.IsValid(body + c)).ToList();
        Assert.Single(valid);
        // その有効な番号の下1桁を別の数字に変えると無効になる。
        int good = valid[0];
        Assert.False(MyNumberValidator.IsValid(body + ((good + 1) % 10)));
    }

    [Fact]
    public void リポジトリ_暗号化して保存し復号で一致_監査ログが残る()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var protector = new MyNumberProtector(new byte[32]);   // テスト用固定鍵
        var audit = new AuditLog(conn);
        var repo = new MyNumberRepository(conn, protector, audit);
        repo.EnsureSchema();

        var op = Principal.Of("test", "テスト管理者", Role.管理者);
        string 本人 = ValidNumber("11111111111");
        string 扶養 = ValidNumber("22222222222");
        repo.Save(new MyNumberRecord
        {
            Number = "5",
            本人 = 本人,
            配偶者 = null,
            扶養親族 = new[] { 扶養, null, null, null, null, null, null },
        }, op);

        // 格納列は平文ではない（列暗号化されている）。
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT 本人 FROM T_マイナンバー情報 WHERE Number='5'";
            string stored = (string)cmd.ExecuteScalar()!;
            Assert.NotEqual(本人, stored);
        }

        var rec = repo.GetDecrypted("5", op);
        Assert.Equal(本人, rec.本人);
        Assert.Equal(扶養, rec.扶養親族[0]);
        Assert.Null(rec.配偶者);

        // マスキングは下4桁のみ表示。
        var masked = repo.GetMasked("5")!;
        Assert.EndsWith(本人[^4..], masked.本人);
        Assert.StartsWith("********", masked.本人);

        // 参照・更新が監査ログに記録されている。
        Assert.True(audit.Count() >= 2);
    }

    [Fact]
    public void リポジトリ_復号権限が無ければ拒否()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var repo = new MyNumberRepository(conn, new MyNumberProtector(new byte[32]), new AuditLog(conn));
        repo.EnsureSchema();
        var op = Principal.Of("payroll", "給与担当", Role.給与担当);   // 復号権限なし
        repo.Save(new MyNumberRecord { Number = "1", 本人 = ValidNumber("33333333333") }, op);

        Assert.Throws<UnauthorizedAccessException>(() => repo.GetDecrypted("1", op));
    }

    [Fact]
    public void 操作ログ_新しい順に取得できる()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var audit = new AuditLog(conn);
        audit.EnsureSchema();

        void Insert(string 日時, string 詳細)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO T_操作ログ (日時,アカウントID,操作者,処理,取扱,詳細) VALUES ($d,'id','op','マイナンバー','更新',$x)";
            cmd.Parameters.AddWithValue("$d", 日時);
            cmd.Parameters.AddWithValue("$x", 詳細);
            cmd.ExecuteNonQuery();
        }
        Insert("2026/06/30 09:00:00.000", "古い");
        Insert("2026/06/30 11:00:00.000", "新しい");
        Insert("2026/06/30 10:00:00.000", "中間");

        var rows = audit.List();
        Assert.Equal(3, rows.Count);
        Assert.Equal("新しい", rows[0].詳細);   // 日時 DESC
        Assert.Equal("中間", rows[1].詳細);
        Assert.Equal("古い", rows[2].詳細);
        Assert.Equal(3, audit.Count());
    }

    // 上位11桁から有効な12桁マイナンバーを作る（テスト用）。
    private static string ValidNumber(string body11)
    {
        int c = Enumerable.Range(0, 10).First(d => MyNumberValidator.IsValid(body11 + d));
        return body11 + c;
    }
}
