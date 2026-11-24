using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Domain.Reference;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 手順1：現行（デスクトップ版）の暗号化SQLiteを復号して別表・標準報酬月額表を読み出し、
/// 移行先ドメインの計算と突合する。現行DBが見つからない環境ではスキップする。
/// </summary>
public class LegacyFidelityTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public LegacyFidelityTests()
    {
        if (!File.Exists(LegacyDbPath)) return;

        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_fidelity_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;

    [SkippableFact]
    public void 現行DBの別表第一から第四が読み出せ_行数と境界値が一致する()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        WithholdingTaxTables t = _repo!.LoadWithholdingTaxTables();

        Assert.Equal(5, t.給与所得控除.Count);   // 令和7年度改正で給与所得控除の区分が6→5
        Assert.Equal(5, t.基礎控除.Count);   // 令和7年度改正で月次源泉の基礎控除区分が4→5
        Assert.Equal(7, t.税率速算.Count);
        Assert.Equal(31667m, t.扶養控除単価);

        // 税率速算の先頭区分：5.105% / 控除0、上限は包含的162500を排他162501へ変換
        Assert.Equal(0.05105m, t.税率速算[0].税率);
        Assert.Equal(162501m, t.税率速算[0].上限);
    }

    [SkippableFact]
    public void PoCのSampleData別表が現行DBの別表と一致する_サンプル妥当性()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        WithholdingTaxTables db = _repo!.LoadWithholdingTaxTables();
        WithholdingTaxTables sample = SampleData.TaxTables;

        Assert.Equal(sample.税率速算.Count, db.税率速算.Count);
        for (int i = 0; i < db.税率速算.Count; i++)
        {
            Assert.Equal(sample.税率速算[i].税率, db.税率速算[i].税率);
            Assert.Equal(sample.税率速算[i].控除額, db.税率速算[i].控除額);
            Assert.Equal(sample.税率速算[i].上限, db.税率速算[i].上限);
        }
        Assert.Equal(sample.扶養控除単価, db.扶養控除単価);
    }

    [SkippableFact]
    public void 標準報酬月額表_報酬30万の保険料が現行値と一致する()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        SocialInsuranceTable table = _repo!.LoadSocialInsuranceTable();
        var calc = new TableBasedSocialInsuranceCalculator(table);

        // 報酬月額 300,000 → 健保14,865 / 介護2,385 / 厚年27,450（現行DB実測値・令和料率）
        SocialInsuranceResult r = calc.Calculate(報酬月額: 300_000m, 総支給額: 310_000m, 介護保険対象: true);

        Assert.Equal(14_865m, r.健康保険);
        Assert.Equal(2_385m, r.介護保険);
        Assert.Equal(27_450m, r.厚生年金);
        Assert.Equal(1_705m, r.雇用保険); // floor(310000 * 0.0055)
    }

    [SkippableFact]
    public void 現行マスタ注入で給与計算が通り_差引支給の不変条件を満たす()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var calc = new PayrollCalculator(
            new TableBasedSocialInsuranceCalculator(_repo!.LoadSocialInsuranceTable()),
            _repo.LoadWithholdingTaxTables());

        PayrollResult r = calc.Calculate(new PayrollInput
        {
            課税支給合計 = 300_000m,
            非課税支給合計 = 10_000m,
            標準報酬月額 = 300_000m,
            扶養親族等の数 = 1,
            介護保険対象 = false,
        });

        Assert.Equal(14_865m, r.社会保険.健康保険);
        Assert.Equal(27_450m, r.社会保険.厚生年金);
        Assert.Equal(r.総支給額 - r.控除合計, r.差引支給額);
        Assert.True(r.所得税 > 0m);
    }

    public void Dispose()
    {
        _conn?.Dispose();
        if (_tmp is not null && File.Exists(_tmp))
        {
            try { File.Delete(_tmp); } catch { /* 一時ファイルの削除失敗は無視 */ }
        }
    }
}
