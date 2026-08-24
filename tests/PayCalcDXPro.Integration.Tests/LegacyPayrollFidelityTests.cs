using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Data.Payroll;
using PayCalcDXPro.Data.Rate;
using PayCalcDXPro.Domain.Reference;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 計算ブリッジ（<see cref="PayrollProcessor"/>）の忠実性検証。
///
/// 当初は現行 T_給与トランの保存済み行を入力から再計算して突合する計画だったが、
/// 開発用DBは従業員マスタ（報酬月額・源泉扶養親族数・料率）が過去トラン保存後に
/// 編集されており（例：Number=1 の報酬月額は現在 980,000＝最高等級。トランは標準報酬≈500,000・扶養8 で保存）、
/// 現行マスタからの再計算で過去トランを再現することは原理的に不可能。
///
/// そこで「実DBの参照テーブル（保険料額表）」を真として用い、既知の正値で再計算が一致することを検証する。
/// 端数処理（50銭）チェーンなど計算式の網羅検証は <see cref="PayrollProcessorTests"/>（合成データ）で行う。
/// 現行DBが見つからない環境ではスキップする。
/// </summary>
public class LegacyPayrollFidelityTests : IDisposable
{
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\bin\DB\PayCalc2026.db";

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;

    public LegacyPayrollFidelityTests()
    {
        if (!File.Exists(LegacyDbPath)) return;

        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_payroll_fidelity_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
    }

    private bool Available => _conn is not null;

    [SkippableFact]
    public void 実DBの保険料額表で報酬月額30万の社会保険が現行値と一致する()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var rateRepo = new RateRepository(_conn!);
        var bundle = new RateBundle { 料率 = rateRepo.Load(), 額表 = rateRepo.LoadTable() };
        var processor = new PayrollProcessor(bundle, SampleData.TaxTables);

        // 報酬月額 300,000・総支給 310,000・甲欄。健保/介護/厚年は現行DB(bin/DB)の保険料額表
        // No=22（報酬月額 290,000〜310,000）の格納値を直読み＋50銭処理した忠実値。
        //   健康保険=14865（端数0）／介護保険=round50(14865+2385=17250)−14865=2385／厚生年金=27450
        var e = new PayrollEntry { Number = "T", 支給回 = 1, 基本給 = 300_000, 課税通勤費 = 10_000 };
        var emp = new PayrollEmployeeSettings
        {
            健康保険加入 = true, 介護保険加入 = true, 厚生年金加入 = true, 雇用保険加入 = true,
            報酬月額 = 300_000, 源泉区分 = 源泉区分.甲, 源泉扶養親族数 = 1, 労働保険適用 = "1", 端数処理 = 2,
        };

        processor.Apply(e, emp);

        Assert.Equal(310_000, e.総支給額);
        Assert.Equal(14_865, e.健康保険);
        Assert.Equal(2_385, e.介護保険);
        Assert.Equal(27_450, e.厚生年金);
        Assert.Null(e.年金基金);               // 未加入は算出しない
        Assert.True(e.雇用保険 > 0);
        Assert.Equal((e.健康保険 ?? 0) + (e.介護保険 ?? 0) + (e.厚生年金 ?? 0) + (e.雇用保険 ?? 0), e.社会保険合計);
        Assert.Equal(310_000 - e.社会保険合計, e.課税対象額); // 非課税なし
        Assert.True(e.所得税 > 0);
        // 差引＝総支給 − 総控除（他控除なし）。
        Assert.Equal(e.社会保険合計 + e.所得税 + 0, e.総控除額);
        Assert.Equal(e.総支給額 - e.総控除額, e.差引支給額);
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
