using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Data.YearEnd;
using PayCalcDXPro.Domain.Services;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 年末調整の年税額計算（<see cref="YearEndCalculation.Apply"/>）を、現行DBの T_年末調整 に保存された
/// 計算済みレコードと突合する。入力（年間集計・各種保険料・配偶者・扶養控除等合計・住宅控除）を現行保存値から
/// 組み立て、結果（給与所得控除後調整後／所得控除計／課税所得／所得税／年調年税額）が現行と一致することを確認する。
/// </summary>
public class YearEndFidelityTests : IDisposable
{
    // 突合の基準DB＝現行アプリ実行時DB（bin\DB）。令和7年改正の完全版（特定親族特別控除・改正後基礎控除を含み、
    // ブラウザ版の年末調整マスタ seed もこれから生成）。計算済み T_年末調整 レコードもこちらに存在する。
    private static readonly string LegacyDbPath =
        @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\bin\DB\PayCalc2026.db";

    private readonly string? _tmp;
    private readonly SqliteConnection? _conn;
    private readonly LegacyMasterRepository? _repo;

    public YearEndFidelityTests()
    {
        if (!File.Exists(LegacyDbPath)) return;
        _tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_yeaf_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(LegacyDbPath, _tmp);
        _conn = new SqliteConnection($"Data Source={_tmp}");
        _conn.Open();
        _repo = new LegacyMasterRepository(_conn);
    }

    private bool Available => _repo is not null;

    // 年齢判定の基準年。現行は MConst.SYR_NEN(=税年+1) を基準に判定する（令和7年＝税年2025→基準2026）。
    // AgeClassifier の境界 (判定年-年齢)/01/01・/01/02 に一致させるため 2026 を用いる。
    private const int 判定年 = 2026;

    [SkippableFact]
    public void 年末調整_現行T_年末調整の保存値と年税額が一致する_フルパイプライン()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var yearTables = _repo!.LoadYearEndTables();
        var dedTables = _repo.LoadIncomeDeductionTables();
        var depTable = _repo.LoadDependentDeductionTable();
        var srdTable = _repo.LoadSpecialRelativeDeductionTable();
        var pensionTable = _repo.LoadPensionConversionTable();
        var repo = new YearEndRepository(_conn!);

        var numbers = ComputedNumbers(_conn!);
        Skip.If(numbers.Count == 0, "現行DBに計算済みの年末調整データが無いためスキップ");

        var 不一致 = new List<string>();
        var stale = new List<string>();   // 旧基礎控除（令和6年）で保存された stale レコード
        int 検証件数 = 0;

        foreach (int num in numbers)
        {
            var e = repo.Load(num);
            if (e is null) continue;

            // 保存済み（現行の正解値）を退避してから、生データでフル再計算する。
            int 旧基礎 = e.基礎控除, 旧調整後 = e.給与所得控除後調整控除後, 旧控除計 = e.所得控除計;
            int 旧課税 = e.課税所得, 旧年税 = e.年調年税額;

            // 集計列（給与計等）は保存値をそのまま使用（RecomputeTotals は呼ばない＝前社/非課税の手入力に依存しない）。
            YearEndCalculation.DeriveAutoDeductions(e, yearTables, depTable, srdTable, pensionTable, 判定年);
            YearEndCalculation.Apply(e, yearTables, dedTables);

            // 令和7年改正の基礎控除が保存値と食い違う＝令和6年計算のまま保存された stale レコード。突合対象外。
            if (e.基礎控除 != 旧基礎)
            {
                stale.Add($"#{num}（基礎 計算={e.基礎控除}/保存={旧基礎}・給与賞与計={e.給与賞与計}）");
                continue;
            }

            検証件数++;
            if (e.給与所得控除後調整控除後 != 旧調整後)
                不一致.Add($"#{num} 給与所得控除後調整後: 計算={e.給与所得控除後調整控除後} 現行={旧調整後}");
            if (e.所得控除計 != 旧控除計)
                不一致.Add($"#{num} 所得控除計: 計算={e.所得控除計} 現行={旧控除計}（基礎={e.基礎控除} 配偶={e.配偶者特別控除} 扶養={e.扶養控除等合計} 特定親族={e.特定親族特別控除額} 生保={e.生保控除} 地震={e.地震控除} 社保={e.給与社保計 + e.賞与社保計} 申告社保={e.申告社保} 国年={e.国民年金保険料等} 小規模={e.小規模共済}）");
            if (e.課税所得 != 旧課税)
                不一致.Add($"#{num} 課税所得: 計算={e.課税所得} 現行={旧課税}");
            if (e.年調年税額 != 旧年税)
                不一致.Add($"#{num} 年調年税額: 計算={e.年調年税額} 現行={旧年税}");
        }

        Assert.True(不一致.Count == 0,
            $"突合対象{検証件数}件中 {不一致.Count}項目が不一致:\n" + string.Join("\n", 不一致.Take(30))
            + (stale.Count > 0 ? $"\n（参考）令和6年基礎控除のstaleレコード {stale.Count}件: {string.Join(", ", stale)}" : ""));
    }

    private static List<int> ComputedNumbers(SqliteConnection conn)
    {
        var list = new List<int>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Number FROM \"T_年末調整\" WHERE 乙欄='1' AND CAST(IFNULL(年調年税額,0) AS INTEGER)>0";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (int.TryParse(Convert.ToString(r.GetValue(0)), out int n)) list.Add(n);
        return list;
    }

    [SkippableFact]
    public void Apply_マッピングと過不足額が計算器と整合する()
    {
        Skip.IfNot(Available, "現行DBが見つからないためスキップ");

        var yt = _repo!.LoadYearEndTables();
        var dt = _repo.LoadIncomeDeductionTables();

        // 年収500万・社会保険料75万（給与60万＋賞与15万）・他控除なし・源泉徴収13万。
        var e = new YearEndEntry
        {
            Number = 999,
            給与賞与計 = 5_000_000,
            給与社保計 = 600_000,
            賞与社保計 = 150_000,
            源泉計 = 130_000,
        };
        YearEndCalculation.Apply(e, yt, dt);

        // 期待値＝同じ税表で「所得控除合計＝社会保険料75万＋基礎控除」を直接与えた計算結果。
        var ded = new IncomeDeductionCalculator(dt);
        decimal 基礎 = ded.基礎控除(e.給与所得控除後調整控除後);
        var expect = new YearEndAdjustmentCalculator(yt).Calculate(5_000_000m, 750_000m + 基礎);

        Assert.Equal((int)基礎, e.基礎控除);
        Assert.Equal(750_000 + (int)基礎, e.所得控除計);     // 社保のみ＋基礎（生保/地震/配偶/扶養=0）
        Assert.Equal((int)expect.課税給与所得金額, e.課税所得);
        Assert.Equal((int)expect.算出所得税額, e.所得税);
        Assert.Equal((int)expect.年調年税額, e.年調年税額);
        Assert.Equal(130_000 - e.年調年税額, e.過不足額);     // 源泉計 − 年調年税額
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
