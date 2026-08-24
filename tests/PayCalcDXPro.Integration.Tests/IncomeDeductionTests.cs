using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 年末調整の所得控除明細（基礎控除・配偶者控除・生命保険料控除）を現行DBの実表で突合し、
/// 控除明細から年税額まで完結することを確認する。
/// </summary>
public class IncomeDeductionTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public IncomeDeductionTests()
    {
        if (!File.Exists(LegacyDbPath)) return;
        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_ded_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;
    private IncomeDeductionCalculator Calc() => new(_repo!.LoadIncomeDeductionTables());

    [SkippableFact]
    public void 基礎控除_合計所得2400万以下は48万円()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        Assert.Equal(480_000m, Calc().基礎控除(3_560_000m));
        Assert.Equal(320_000m, Calc().基礎控除(24_300_000m)); // 2400万超〜2450万
        Assert.Equal(0m, Calc().基礎控除(30_000_000m));        // 2500万超
    }

    [SkippableFact]
    public void 配偶者控除_専業主婦かつ本人900万以下は38万円()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        var c = Calc();
        Assert.Equal(380_000m, c.配偶者控除(配偶者あり: true, 配偶者合計所得: 0m, 配偶者70歳以上: false, 本人合計所得: 3_560_000m));
        // 70歳以上（老人控除対象配偶者）は48万円
        Assert.Equal(480_000m, c.配偶者控除(true, 0m, true, 3_560_000m));
        // 本人所得1000万超は0円
        Assert.Equal(0m, c.配偶者控除(true, 0m, false, 10_500_000m));
        // 配偶者なしは0円
        Assert.Equal(0m, c.配偶者控除(false, 0m, false, 3_560_000m));
    }

    [SkippableFact]
    public void 生命保険料控除_介護医療と一般新で算定し合計上限内()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        var c = Calc();

        // 介護医療5万 → 区分40001-80000(率25%,控除2万) → Ceiling(50000×25% + 20000) = 32500
        Assert.Equal(32_500m, c.生命保険料控除(50_000m, 0m, 0m, 0m, 0m));

        // 一般新4万 → 区分20001-40000(率50%,控除1万) → Ceiling(40000×50% + 10000) = 30000
        Assert.Equal(30_000m, c.生命保険料控除(0m, 40_000m, 0m, 0m, 0m));

        // 介護(32,500) + 一般(30,000) = 62,500（12万上限内）
        Assert.Equal(62_500m, c.生命保険料控除(50_000m, 40_000m, 0m, 0m, 0m));
    }

    [SkippableFact]
    public void 年末調整_控除明細から年税額まで完結する()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var yearEnd = new YearEndAdjustmentCalculator(_repo!.LoadYearEndTables());
        var input = new YearEndDeductionInput
        {
            年間給与収入 = 5_000_000m,
            社会保険料控除 = 750_000m,
            配偶者あり = true,
            配偶者合計所得 = 0m,
            介護医療保険料 = 50_000m,
        };

        YearEndResult r = yearEnd.CalculateWithDeductions(input, Calc());

        Assert.Equal(3_560_000m, r.給与所得控除後調整後);
        Assert.Equal(480_000m, r.基礎控除);
        Assert.Equal(380_000m, r.配偶者控除);
        Assert.Equal(32_500m, r.生命保険料控除);          // 介護医療5万
        Assert.Equal(1_642_500m, r.所得控除額の合計);     // 750,000 + 32,500 + 380,000 + 480,000
        Assert.Equal(1_917_000m, r.課税給与所得金額);
        Assert.Equal(95_850m, r.算出所得税額);             // 1,917,000 × 5%
        Assert.Equal(97_800m, r.年調年税額);              // ×102.1%、100円未満切捨て
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
