using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 手順3：賞与・年末調整のドメイン化を、現行DBの実テーブルで突合する。
/// </summary>
public class BonusAndYearEndTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public BonusAndYearEndTests()
    {
        if (!File.Exists(LegacyDbPath)) return;
        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_bonus_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;

    [SkippableFact]
    public void 賞与_前月30万扶養0人の率と税額が現行表どおり()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var calc = new BonusCalculator(_repo!.LoadBonusTaxRateTable());

        // 前月給与(社保控除後)30万・扶養0人 → 賞与率 8.168%（現行 T_賞与源泉徴収税額甲）
        BonusResult r = calc.Calculate(
            賞与課税支給: 500_000m, 賞与社会保険料: 70_000m,
            前月社保控除後給与: 300_000m, 扶養親族等の数: 0);

        Assert.Equal(8.168m, r.賞与に乗ずる率);
        Assert.Equal(430_000m, r.課税対象額);
        Assert.Equal(35_122m, r.賞与所得税); // floor(430000 * 8.168%)
    }

    [SkippableFact]
    public void 賞与_前月給与が下限未満なら率0かつ税額0()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var calc = new BonusCalculator(_repo!.LoadBonusTaxRateTable());
        BonusResult r = calc.Calculate(300_000m, 40_000m, 前月社保控除後給与: 50_000m, 扶養親族等の数: 0);

        Assert.Equal(0m, r.賞与に乗ずる率);
        Assert.Equal(0m, r.賞与所得税);
    }

    [SkippableFact]
    public void 年末調整_年収500万_所得控除150万の年税額が期待値どおり()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var calc = new YearEndAdjustmentCalculator(_repo!.LoadYearEndTables());
        YearEndResult r = calc.Calculate(年間給与収入: 5_000_000m, 所得控除額の合計: 1_500_000m);

        Assert.Equal(3_560_000m, r.給与所得控除後の金額); // 5,000,000×80% − 440,000
        Assert.Equal(2_060_000m, r.課税給与所得金額);     // − 1,500,000、1000円未満切捨
        Assert.Equal(108_500m, r.算出所得税額);           // 2,060,000×10% − 97,500
        Assert.Equal(110_700m, r.年調年税額);             // ×102.1%、100円未満切捨
    }

    [SkippableFact]
    public void 年末調整_4000円括りで端数収入が切り捨てられる()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var calc = new YearEndAdjustmentCalculator(_repo!.LoadYearEndTables());
        YearEndResult a = calc.Calculate(5_000_000m, 1_500_000m);
        YearEndResult b = calc.Calculate(5_002_000m, 1_500_000m); // 4,000円括りで5,000,000扱い

        Assert.Equal(3_560_000m, b.給与所得控除後の金額);
        Assert.Equal(a.給与所得控除後の金額, b.給与所得控除後の金額);
        Assert.Equal(a.年調年税額, b.年調年税額);
    }

    [SkippableFact]
    public void 年末調整_給与収入900万で所得金額調整控除が適用される()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var calc = new YearEndAdjustmentCalculator(_repo!.LoadYearEndTables());
        YearEndResult r = calc.Calculate(9_000_000m, 2_000_000m, 所得金額調整控除の適用: true);

        Assert.Equal(7_050_000m, r.給与所得控除後の金額); // 9,000,000 − 1,950,000
        Assert.Equal(50_000m, r.所得金額調整控除);          // (9,000,000 − 8,500,000) / 10
        Assert.Equal(7_000_000m, r.給与所得控除後調整後);
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
