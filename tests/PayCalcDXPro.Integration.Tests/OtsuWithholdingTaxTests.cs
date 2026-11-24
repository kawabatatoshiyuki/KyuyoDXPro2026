using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>源泉所得税「乙欄」を現行DB(T_乙税第1〜3表・乙税階差)で突合する。</summary>
public class OtsuWithholdingTaxTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public OtsuWithholdingTaxTests()
    {
        if (!File.Exists(LegacyDbPath)) return;
        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_otsu_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;
    private OtsuWithholdingTaxCalculator Calc() => new(_repo!.LoadOtsuTaxTables());

    [SkippableTheory]
    [InlineData(0, 0, 0)]              // 課税0以下
    [InlineData(100_000, 0, 3_063)]   // 区分1: floor(100,000 × 3.063%)
    [InlineData(100_000, 1, 1_453)]   // 区分1 − 扶養1人(1,610)
    [InlineData(300_000, 0, 53_600)]  // 区分2: 階差＋第1/3表の補間
    [InlineData(800_000, 0, 283_704)] // 区分3: 259,200 + (800,000−740,000)×40.84%
    [InlineData(2_000_000, 0, 788_640)] // 区分4: 655,400 + (2,000,000−1,710,000)×45.945%
    public void 乙欄税額が現行ロジックどおり(int 課税対象, int 扶養, int 期待)
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");
        Assert.Equal(期待, Calc().Calculate(課税対象, 扶養));
    }

    [SkippableFact]
    public void 給与計算で源泉区分乙を指定すると乙欄税額が使われる()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var otsu = Calc();
        var payroll = new PayrollCalculator(
            new TableBasedSocialInsuranceCalculator(_repo!.LoadSocialInsuranceTable()),
            _repo.LoadWithholdingTaxTables(),
            otsu);

        var input = new PayrollInput
        {
            課税支給合計 = 300_000m,
            標準報酬月額 = 300_000m,
            扶養親族等の数 = 0,
            介護保険対象 = false,
            源泉区分 = 源泉区分.乙,
        };

        PayrollResult r = payroll.Calculate(input);

        // 課税対象 = 課税支給 − 社会保険料合計。乙欄計算器の結果と一致するはず。
        decimal 課税対象 = 300_000m - r.社会保険.合計;
        Assert.Equal(otsu.Calculate(課税対象, 0), r.所得税);
        Assert.Equal(r.総支給額 - r.控除合計, r.差引支給額);
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
