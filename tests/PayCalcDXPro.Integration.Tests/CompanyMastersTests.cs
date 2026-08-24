using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Company;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 会社情報に付随する4マスタ（部門/役職/銀行/報告書提出先）の保存→再オープン読み込みが
/// 一貫することと、現行同等の固定先頭レコードと連番採番が行われることを確認する。
/// DPAPI/SQLCipher は Windows 専用のため非 Windows ではスキップする。
/// </summary>
public class CompanyMastersTests : IDisposable
{
    private readonly string _dir;

    public CompanyMastersTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_masters_{Guid.NewGuid():N}");
    }

    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    [Fact]
    public void マスタ4種が保存され再オープン後も同じ内容で読める()
    {
        if (!OperatingSystem.IsWindows()) return;

        var input = new CompanyMasters();
        input.部門.Add(new DepartmentRow { 部門名 = "営業部" });
        input.部門.Add(new DepartmentRow { 部門名 = "" });          // 空行は除外される想定
        input.部門.Add(new DepartmentRow { 部門名 = "製造部" });
        input.役職.Add(new PositionRow { 役職名 = "部長" });
        input.銀行.Add(new BankRow { 銀行名 = "ほげ銀行", 支店名 = "本店" });
        input.銀行.Add(new BankRow { 銀行名 = "ふが銀行", 支店名 = "" }); // 支店名は空→NULL
        input.提出先.Add(new RecipientRow { 市区町村名 = "東京都千代田区", 徴収義務者番号 = "12345" });

        using (var store = Open())
        {
            var repo = new CompanyRepository(store.Connection);
            repo.EnsureSchema();
            repo.SaveMasters(input);
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new CompanyRepository(reopened.Connection);
            repo.EnsureSchema();
            CompanyMasters got = repo.LoadMasters();

            // 空行は除外され、固定先頭レコードは LoadMasters の結果に含まれない
            Assert.Equal(new[] { "営業部", "製造部" }, got.部門.Select(x => x.部門名));
            Assert.Equal(new[] { "部長" }, got.役職.Select(x => x.役職名));
            Assert.Equal(2, got.銀行.Count);
            Assert.Equal("ほげ銀行", got.銀行[0].銀行名);
            Assert.Equal("本店", got.銀行[0].支店名);
            Assert.Equal("ふが銀行", got.銀行[1].銀行名);
            Assert.Equal("", got.銀行[1].支店名); // NULL は空文字に正規化
            Assert.Single(got.提出先);
            Assert.Equal("東京都千代田区", got.提出先[0].市区町村名);
            Assert.Equal("12345", got.提出先[0].徴収義務者番号);

            // 固定先頭レコードと連番採番（現行同等）を直接検証
            Assert.Equal("未設定", Scalar(reopened.Connection, "SELECT 部門名 FROM M_部門情報 WHERE 部門番号='0'"));
            Assert.Equal("営業部", Scalar(reopened.Connection, "SELECT 部門名 FROM M_部門情報 WHERE 部門番号='1'"));
            Assert.Equal("製造部", Scalar(reopened.Connection, "SELECT 部門名 FROM M_部門情報 WHERE 部門番号='2'"));
            Assert.Equal("現金支給", Scalar(reopened.Connection, "SELECT 銀行名 FROM M_銀行情報 WHERE 銀行番号='1'"));
            Assert.Equal("ほげ銀行", Scalar(reopened.Connection, "SELECT 銀行名 FROM M_銀行情報 WHERE 銀行番号='2'"));
            Assert.Equal("特別徴収対象外", Scalar(reopened.Connection, "SELECT 市区町村名 FROM M_報告書提出先情報 WHERE 提出先番号='1'"));
        }
    }

    [Fact]
    public void 再保存で全件置換されゴミが残らない()
    {
        if (!OperatingSystem.IsWindows()) return;

        var first = new CompanyMasters();
        first.部門.Add(new DepartmentRow { 部門名 = "旧部門A" });
        first.部門.Add(new DepartmentRow { 部門名 = "旧部門B" });

        using (var store = Open())
        {
            var repo = new CompanyRepository(store.Connection);
            repo.EnsureSchema();
            repo.SaveMasters(first);

            var second = new CompanyMasters();
            second.部門.Add(new DepartmentRow { 部門名 = "新部門" });
            repo.SaveMasters(second);

            CompanyMasters got = repo.LoadMasters();
            Assert.Equal(new[] { "新部門" }, got.部門.Select(x => x.部門名));
            // 先頭固定行＋ユーザー1行のみ＝2件
            Assert.Equal("2", Scalar(store.Connection, "SELECT COUNT(*) FROM M_部門情報"));
        }
    }

    private static string Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? "";
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
