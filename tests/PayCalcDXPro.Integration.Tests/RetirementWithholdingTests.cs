using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.YearEnd;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>退職所得の源泉徴収票・特別徴収票（T_退職源泉情報）の CRUD を in-memory で検証する。</summary>
public class RetirementWithholdingTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RetirementWithholdingRepository _repo;

    public RetirementWithholdingTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _repo = new RetirementWithholdingRepository(_conn);
        _repo.EnsureSchema();
    }

    [Fact]
    public void 登録は採番されLoadで全項目が往復する()
    {
        var e = new RetirementWithholdingRecord
        {
            提出 = true, 氏名 = "部長　退職太郎", 個人番号 = "123456789012",
            住所 = "東京都港区1-2-3", 年始住所 = "東京都中央区4-5-6",
            退職所得控除額 = 800000, 勤続年数 = "10", 就職年月日 = "2015/04/01", 退職年月日 = "2025/03/31",
            摘要 = "テスト",
        };
        e.支払金額[0] = 3000000; e.源泉徴収税額[0] = 50000; e.市町村民税[0] = 12000; e.道府県民税[0] = 8000;
        e.支払金額[2] = 1000000; e.源泉徴収税額[2] = 20000;

        int no = _repo.Insert(e);
        Assert.Equal(1, no);

        var r = _repo.Load(no)!;
        Assert.True(r.提出);
        Assert.Equal("部長　退職太郎", r.氏名);
        Assert.Equal("123456789012", r.個人番号);
        Assert.Equal("東京都港区1-2-3", r.住所);
        Assert.Equal(800000, r.退職所得控除額);
        Assert.Equal("2025/03/31", r.退職年月日);
        Assert.Equal(3000000, r.支払金額[0]);
        Assert.Equal(8000, r.道府県民税[0]);
        Assert.Equal(1000000, r.支払金額[2]);
    }

    [Fact]
    public void 連番採番される()
    {
        int a = _repo.Insert(new RetirementWithholdingRecord { 氏名 = "A" });
        int b = _repo.Insert(new RetirementWithholdingRecord { 氏名 = "B" });
        Assert.Equal(1, a);
        Assert.Equal(2, b);
        Assert.Equal(2, _repo.List().Count);
    }

    [Fact]
    public void 更新と削除()
    {
        int no = _repo.Insert(new RetirementWithholdingRecord { 氏名 = "旧", 提出 = false });
        var e = _repo.Load(no)!;
        e.氏名 = "新"; e.提出 = true; e.退職所得控除額 = 400000;
        _repo.Update(e);

        var r = _repo.Load(no)!;
        Assert.Equal("新", r.氏名);
        Assert.True(r.提出);
        Assert.Equal(400000, r.退職所得控除額);

        _repo.Delete(no);
        Assert.Null(_repo.Load(no));
        Assert.Empty(_repo.List());
    }

    public void Dispose() => _conn.Dispose();
}
