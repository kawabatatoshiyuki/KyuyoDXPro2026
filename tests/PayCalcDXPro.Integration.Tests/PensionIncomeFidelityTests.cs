using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 公的年金等の所得計算を現行DB(T_速算表)で突合する。現行 calNenkinSyotoku のSQL＋式を
/// テスト側で再現し、ドメイン <see cref="PensionIncomeCalculator"/> の結果と一致することを確認する。
/// </summary>
public class PensionIncomeFidelityTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private const int 判定年 = 2026;

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public PensionIncomeFidelityTests()
    {
        if (!File.Exists(LegacyDbPath)) return;
        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_pen_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;

    // 現行 calNenkinSyotoku を直SQLで再現（突合の基準値）。
    private decimal 現行式(decimal 年金, bool は65以上)
    {
        string ランク = 年金 <= 10_000_000m ? "1" : 年金 <= 20_000_000m ? "2" : "3";
        string 年齢 = は65以上 ? "2" : "1";
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText =
            "select 控除率, 控除額 from T_速算表 " +
            "where 控除後ランク区分=@r and 年齢ランク=@a and 配偶者支給額_自<=@n and 配偶者支給額_至>=@n";
        cmd.Parameters.AddWithValue("@r", ランク);
        cmd.Parameters.AddWithValue("@a", 年齢);
        cmd.Parameters.AddWithValue("@n", 年金);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return 0m;
        decimal 率 = Convert.ToDecimal(r.GetValue(0));
        decimal 控除 = Convert.ToDecimal(r.GetValue(1));
        return Math.Ceiling(年金 * 率 / 100m - 控除);
    }

    [SkippableTheory]
    [InlineData(800_000, false)]
    [InlineData(1_500_000, false)]
    [InlineData(2_500_000, true)]
    [InlineData(4_000_000, true)]
    [InlineData(800_000, true)]
    public void ドメインの年金所得が現行式と一致する(decimal 年金, bool は65以上)
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var calc = new PensionIncomeCalculator(_repo!.LoadPensionConversionTable());
        DateOnly 生年月日 = は65以上 ? new DateOnly(判定年 - 70, 6, 1) : new DateOnly(判定年 - 40, 6, 1);

        decimal expected = 現行式(年金, は65以上);
        decimal actual = calc.年金所得(年金, 生年月日, 判定年);
        Assert.Equal(expected, actual);
    }

    [SkippableFact]
    public void 速算表が読み出せる()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        PensionConversionTable t = _repo!.LoadPensionConversionTable();
        Assert.NotEmpty(t.行);
    }

    public void Dispose()
    {
        _conn?.Dispose();
        if (_tmp is not null && File.Exists(_tmp))
        {
            try { File.Delete(_tmp); } catch { /* 一時ファイル削除失敗は無視 */ }
        }
    }
}
