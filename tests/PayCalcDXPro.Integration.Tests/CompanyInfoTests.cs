using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Company;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 会社情報の保存/読込と、旧スキーマ（郵便番号列なし）への列後付け（EnsureColumn）の検証。
/// DPAPI/SQLCipher は Windows 専用のため非 Windows ではスキップする。
/// </summary>
public class CompanyInfoTests : IDisposable
{
    private readonly string _dir;

    public CompanyInfoTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalc_company_{Guid.NewGuid():N}");
    }

    private SecureDataStore Open()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return new SecureDataStore(_dir, DataProtectionScope.CurrentUser);
    }

    [Fact]
    public void 郵便番号を含む会社情報が往復する()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var store = Open())
        {
            var repo = new CompanyRepository(store.Connection);
            repo.EnsureSchema();
            repo.Save(new CompanyInfo { 会社名 = "テスト商事", 郵便番号 = "1234567", 所在地 = "東京都千代田区" });
        }
        SqliteConnection.ClearAllPools();

        using (var reopened = Open())
        {
            var repo = new CompanyRepository(reopened.Connection);
            repo.EnsureSchema();
            var c = repo.Load()!;
            Assert.Equal("テスト商事", c.会社名);
            Assert.Equal("1234567", c.郵便番号);
            Assert.Equal("東京都千代田区", c.所在地);
        }
    }

    [Fact]
    public void 郵便番号列が無い旧スキーマにも列が後付けされ保存できる()
    {
        if (!OperatingSystem.IsWindows()) return;

        // 旧スキーマ（郵便番号列なし）を手で作る
        using (var store = Open())
        {
            using var cmd = store.Connection.CreateCommand();
            // 移行済みDB相当：郵便番号以外の現行列をすべて持つ旧スキーマ。
            cmd.CommandText = """
                CREATE TABLE "M_会社情報" (
                    "会社番号" INTEGER,
                    "会社名カナ" TEXT, "会社名" TEXT, "事業所名" TEXT,
                    "所在地カナ" TEXT, "所在地" TEXT,
                    "代表者氏名カナ" TEXT, "代表者名" TEXT, "ＴＥＬ" TEXT,
                    "連絡者名カナ" TEXT, "連絡者名" TEXT, "事業種目" TEXT,
                    "番号区分" TEXT, "事業所マイナンバー" TEXT,
                    "税務署名" TEXT, "署番号" TEXT, "整理番号" TEXT,
                    "支払方法" TEXT, "支払金融機関" TEXT, "金融機関所在地" TEXT,
                    "労働保険適用事業所" TEXT, "年末調整方法" TEXT, "残業手当計算端数処理" TEXT,
                    "支給日登録フラグ" TEXT,
                    PRIMARY KEY("会社番号")
                );
                INSERT INTO "M_会社情報" (会社番号, 会社名, 所在地) VALUES (1, '旧データ', '大阪市');
                """;
            cmd.ExecuteNonQuery();

            // EnsureSchema が郵便番号列を ALTER で追加するはず
            var repo = new CompanyRepository(store.Connection);
            repo.EnsureSchema();

            var c = repo.Load()!;
            Assert.Equal("旧データ", c.会社名);
            Assert.Equal("", c.郵便番号); // 既存行は NULL → 空

            c.郵便番号 = "5400001";
            repo.Save(c);
            Assert.Equal("5400001", repo.Load()!.郵便番号);
        }
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
