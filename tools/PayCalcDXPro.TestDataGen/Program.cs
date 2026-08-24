using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PayCalcDXPro.Data;
using PayCalcDXPro.Data.Bonus;
using PayCalcDXPro.Data.Company;
using PayCalcDXPro.Data.Employee;
using PayCalcDXPro.Data.Legacy;
using PayCalcDXPro.Data.PayDay;
using PayCalcDXPro.Data.PayItem;
using PayCalcDXPro.Data.Payroll;
using PayCalcDXPro.Data.Rate;
using PayCalcDXPro.Data.TaxTable;
using PayCalcDXPro.Data.YearEnd;
using PayCalcDXPro.Domain.Services;

// ============================================================
// 給与計算DXPro テストデータ作成ツール
//   従業員・給与・賞与・年末調整(T_年末調整) のテストデータを移行済みDB（SQLCipher）へ投入する。
//   給与・賞与の金額は本番と同じ計算ロジック（PayrollProcessor / BonusProcessor）で算出する。
//
// 使い方:
//   dotnet run --project tools/PayCalcDXPro.TestDataGen -- [options]
//   --count N      作成する従業員数（既定 5）
//   --out <dir>    出力先データフォルダ（既定 %LocalAppData%\PayCalcDXPro\data ＝開発DB）
//   --scope u|m    DPAPIスコープ user/machine（既定 user）
//   --clean        既存のテストデータ（No が "TEST" で始まる従業員＋そのトラン/年調）を削除して終了
//   --prefix P     テスト従業員の No 接頭辞（既定 TEST）。識別・クリーンに使用
// ============================================================

Console.OutputEncoding = Encoding.UTF8;
if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Windows専用です（DPAPI暗号化DB）。"); return 1; }

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
}
bool Flag(string name) => Array.IndexOf(args, name) >= 0;

int count = int.TryParse(Arg("--count"), out int c) ? Math.Max(1, c) : 5;
string prefix = Arg("--prefix") ?? "TEST";
string dataDir = Arg("--out")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PayCalcDXPro", "data");
var scope = (Arg("--scope") ?? "user").StartsWith("m", StringComparison.OrdinalIgnoreCase)
    ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;

Console.WriteLine("=== 給与計算DXPro テストデータ作成 ===");
Console.WriteLine($"出力先 : {dataDir}");

if (!SecureDataStore.Exists(dataDir))
{
    Console.Error.WriteLine("出力先に移行済みDB（PayCalcDXPro.db）がありません。先に移行ツールで作成してください。");
    return 1;
}

using var store = new SecureDataStore(dataDir, scope);
var conn = store.Connection;

// 各リポジトリ（スキーマ整備）。
var empRepo = new EmployeeRepository(conn); empRepo.EnsureSchema();
var payrollRepo = new PayrollTranRepository(conn); payrollRepo.EnsureSchema();
var bonusRepo = new BonusTranRepository(conn); bonusRepo.EnsureSchema();
new CompanyRepository(conn).EnsureSchema();
var rateRepo = new RateRepository(conn); rateRepo.EnsureSchema();
new TaxTableRepository(conn).EnsureSchema();

if (Flag("--clean")) { Clean(); return 0; }

// ---- 計算器（料率・額表・税表はDB全体で共通なので1度だけ構築）----
var bundle = new RateBundle { 料率 = rateRepo.Load(), 額表 = rateRepo.LoadTable() };
if (bundle.額表.Count == 0)
    Console.WriteLine("⚠ 料率保険料額表が空です。社会保険料が0で算出されます（移行済みDB推奨）。");

var legacy = new LegacyMasterRepository(conn);
var 甲表 = legacy.LoadWithholdingTaxTables();
OtsuWithholdingTaxCalculator? 乙 = null;
try { 乙 = new OtsuWithholdingTaxCalculator(legacy.LoadOtsuTaxTables()); } catch { }
var payProc = new PayrollProcessor(bundle, 甲表, 乙);

var 賞与料率 = rateRepo.Load();
var 賞与甲 = legacy.LoadBonusTaxRateTable();
PayCalcDXPro.Domain.Models.BonusOtsuRateTable? 賞与乙 = null;
try { 賞与乙 = legacy.LoadBonusOtsuRateTable(); } catch { }
var bonusProc = new BonusProcessor(賞与料率, 賞与甲, 賞与乙);

var company = new CompanyRepository(conn).Load() ?? new CompanyInfo();
bool[] 非課税手当 = new PayItemRepository(conn).Load().給与手当.Select(a => a.非課税).ToArray();
if (非課税手当.Length < 12) Array.Resize(ref 非課税手当, 12);

var payday = new PayDayRepository(conn).Load();
int 賞与回数 = Math.Clamp(payday.賞与.Count(b => b.支給月 is not null), 0, 4);
if (賞与回数 == 0) 賞与回数 = 2;   // 支給日未登録でも賞与テストデータは2回ぶん作る

// 報酬月額のバリエーション（額表の等級に当たる代表値）。
int[] 報酬候補 = { 200000, 220000, 240000, 260000, 280000, 300000, 320000, 360000, 400000, 440000 };

Console.WriteLine($"従業員 {count} 人 ／ 給与 支給回1〜12 ／ 賞与 1〜{賞与回数}回 ／ 年末調整(T_年末調整) を作成します。\n");

// 年末調整（T_年末調整）はアプリの YearEndRepository で読み書きする。計算は YearEndCalculation。
var yeaRepo = new YearEndRepository(conn); yeaRepo.EnsureSchema();
new YearEndMasterRepository(conn).EnsureSeeded();
var yearTables = legacy.LoadYearEndTables();
var dedTables = legacy.LoadIncomeDeductionTables();
var depTable = legacy.LoadDependentDeductionTable();
var srTable = legacy.LoadSpecialRelativeDeductionTable();
var pensionTable = legacy.LoadPensionConversionTable();
int 判定年 = payday.支給年 + 1;   // 年齢判定の基準年（税年+1）
var scenarioLog = new List<string>();

int empCreated = 0, payCreated = 0, bonusCreated = 0, yeaCreated = 0;

for (int i = 1; i <= count; i++)
{
    var e = BuildEmployee(i);
    int number = empRepo.Save(e);
    e.Number = number;
    empCreated++;

    var settings = PayrollSettings(e);
    var bonusSettings = BonusSettings(e);

    // ---- 給与（支給回 1〜12）----
    for (int 支給回 = 1; 支給回 <= 12; 支給回++)
    {
        var entry = BuildPayroll(number.ToString(), 支給回, e);
        payProc.Apply(entry, settings);
        payrollRepo.Save(entry);
        payCreated++;
    }

    // ---- 賞与（1〜賞与回数）----
    var 賞与結果 = new Dictionary<int, BonusEntry>();
    for (int 回 = 1; 回 <= 賞与回数; 回++)
    {
        var b = BuildBonus(number.ToString(), 回, e);
        var ctx = new BonusContext
        {
            既支給賞与総支給 = bonusRepo.ListEarlierTotals(number.ToString(), 回),
            前月給与課税対象額 = PrevMonthTaxable(number.ToString(), 回),
        };
        bonusProc.Apply(b, bonusSettings, ctx);
        bonusRepo.Save(b);
        賞与結果[回] = b;
        bonusCreated++;
    }

    // ---- 年末調整（T_年末調整）：ケース別に設定して計算・保存 ----
    string sc = SeedYearEnd(number, i, e);
    yeaCreated++;

    Console.WriteLine($"  [{i}/{count}] No={e.No} {e.氏名}  Number={number}  ケース：{sc}");
    scenarioLog.Add($"    No={e.No}  {sc}");
}

Console.WriteLine();
Console.WriteLine("── 源泉徴収票 検証ケース一覧 ──");
foreach (var s in scenarioLog) Console.WriteLine(s);
Console.WriteLine();
Console.WriteLine($"完了：従業員 {empCreated} ／ 給与トラン {payCreated} ／ 賞与トラン {bonusCreated} ／ 年末調整 {yeaCreated}");
Console.WriteLine($"削除する場合： dotnet run --project tools/PayCalcDXPro.TestDataGen -- --clean --prefix {prefix}");
return 0;

// ============================================================
// 生成ロジック
// ============================================================

Employee BuildEmployee(int i)
{
    // ケース1（所得金額調整控除・同配/調整摘要の検証）は給与所得控除後>1000万になるよう高給に。
    int 報酬 = i == 1 ? 900000 : 報酬候補[(i - 1) % 報酬候補.Length];
    // 乙税表（給与・賞与とも月次）が在るときだけ乙欄をばらつかせる。無いDBでは全員甲欄。
    bool 乙欄 = 乙 is not null && 賞与乙 is not null && i % 5 == 0;
    bool 介護 = i % 2 == 0;                        // 偶数を介護加入
    int birthYear = 1965 + (i % 30);              // 年齢にばらつき
    return new Employee
    {
        Number = 0,
        No = $"{prefix}{i:000}",
        氏名 = $"テスト {(i % 2 == 0 ? "花子" : "太郎")}{i}",
        フリガナ = "テスト タロウ",
        郵便番号 = "1000001",
        住所１ = "東京都千代田区千代田1-1",
        住所２ = $"テストビル{i}号室",
        部門番号 = "0",
        役職番号 = "0",
        性別コード = i % 2 == 0 ? "2" : "1",
        生年月日 = $"{birthYear}/04/15",
        就退区分 = "1",
        銀行番号 = "1",
        預金種類 = "1",
        口座番号 = $"{1000000 + i}",
        健康保険 = "1",
        介護保険 = 介護 ? "1" : "2",
        子育支援 = "1",
        厚生年金 = "1",
        年金基金 = "2",
        雇用保険 = "1",
        所得税源泉 = 乙欄 ? "2" : "1",
        本人障害者 = "1",
        本人寡婦夫 = "1",
        本人勤労学生 = "1",
        配偶者区分 = i % 3 == 0 ? "2" : "1",
        配偶者障害者 = "1",
        配偶者合計所得見積 = "1",
        扶養親族親族数 = i % 4,
        住民税1_5月 = 10000,
        住民税6月 = 12000,
        住民税7_12月 = 10000,
        報酬月額 = 報酬,
        日給単価 = 報酬 / 21,
        法定内残業単価 = (int)(報酬 / 173.0),
        普通残業単価 = (int)(報酬 / 173.0 * 1.25),
        普通残業単価60h超 = (int)(報酬 / 173.0 * 1.50),
        深夜残業単価 = (int)(報酬 / 173.0 * 1.50),
        深夜残業単価60h超 = (int)(報酬 / 173.0 * 1.75),
        休日残業単価 = (int)(報酬 / 173.0 * 1.35),
        休日残業単価60h超 = (int)(報酬 / 173.0 * 1.35),
        休日深夜残業単価 = (int)(報酬 / 173.0 * 1.60),
        休日深夜残業単価60h超 = (int)(報酬 / 173.0 * 1.60),
    };
}

PayrollEmployeeSettings PayrollSettings(Employee e) => new()
{
    健康保険加入 = e.健康保険 == "1",
    介護保険加入 = e.介護保険 == "1",
    子育支援加入 = e.子育支援 == "1",
    厚生年金加入 = e.厚生年金 == "1",
    年金基金加入 = e.年金基金 == "1",
    雇用保険加入 = e.雇用保険 == "1",
    報酬月額 = e.報酬月額,
    労働保険適用 = company.労働保険適用事業所,
    源泉区分 = e.所得税源泉 == "2" ? 源泉区分.乙 : e.所得税源泉 == "3" ? 源泉区分.計算しない : 源泉区分.甲,
    源泉扶養親族数 = e.源泉税額計算の扶養親族数,
    端数処理 = int.TryParse(company.残業手当計算端数処理, out int h) ? h : 2,
    日給単価 = e.日給単価,
    法定内残業単価 = e.法定内残業単価,
    普通残業単価 = e.普通残業単価, 普通残業単価60h超 = e.普通残業単価60h超,
    深夜残業単価 = e.深夜残業単価, 深夜残業単価60h超 = e.深夜残業単価60h超,
    休日残業単価 = e.休日残業単価, 休日残業単価60h超 = e.休日残業単価60h超,
    休日深夜残業単価 = e.休日深夜残業単価, 休日深夜残業単価60h超 = e.休日深夜残業単価60h超,
    非課税手当 = 非課税手当,
};

BonusEmployeeSettings BonusSettings(Employee e) => new()
{
    健康保険加入 = e.健康保険 == "1",
    介護保険加入 = e.介護保険 == "1",
    子育支援加入 = e.子育支援 == "1",
    厚生年金加入 = e.厚生年金 == "1",
    年金基金加入 = e.年金基金 == "1",
    雇用保険加入 = e.雇用保険 == "1",
    労働保険適用 = company.労働保険適用事業所,
    源泉区分 = e.所得税源泉 == "2" ? 源泉区分.乙 : e.所得税源泉 == "3" ? 源泉区分.計算しない : 源泉区分.甲,
    源泉扶養親族数 = e.源泉税額計算の扶養親族数,
};

PayrollEntry BuildPayroll(string number, int 支給回, Employee e)
{
    bool 残業あり = 支給回 % 3 == 0;
    double 普通残業 = 残業あり ? 8.0 : 0.0;
    int 時間外 = (int)(普通残業 * e.普通残業単価);
    return new PayrollEntry
    {
        Number = number,
        支給回 = 支給回,
        基本給 = e.報酬月額,
        給与支給手当 = new int?[12] { 10000, 5000, null, null, null, null, null, null, null, null, null, null },
        時間外手当 = 時間外,
        勤怠控除 = 0,
        課税通勤費 = 0,
        非課税通勤費 = 10000,
        住民税 = 支給回 == 6 ? e.住民税6月 : (支給回 <= 5 ? e.住民税1_5月 : e.住民税7_12月),
        所定出勤日数 = 22,
        出勤日数 = 22,
        労働時間 = 160,
        有給休暇 = 支給回 % 4 == 0 ? 1 : 0,
        有給残日数 = 20,
        普通残業60h以下 = 普通残業,
        備考 = "テストデータ",
    };
}

BonusEntry BuildBonus(string number, int 回, Employee e) => new()
{
    Number = number,
    回 = 回,
    賞与 = e.報酬月額 * 2,
    賞与支給 = new int?[16],
    控除項目 = new int?[9],
    備考 = "テストデータ",
};

int? PrevMonthTaxable(string number, int 回)
{
    int? 賞与支給月 = (回 >= 1 && 回 <= payday.賞与.Count) ? payday.賞与[回 - 1].支給月 : null;
    if (賞与支給月 is null) return null;
    int 前月 = 賞与支給月.Value - 1;
    for (int k = 0; k < payday.月別.Count; k++)
        if (payday.月別[k].支給月 == 前月)
            return payrollRepo.Load(number, k + 1)?.課税対象額;
    return null;
}

// ============================================================
// 年末調整（T_年末調整）の検証ケース投入
//   源泉徴収票の各派生値（特定親族区分・非居住者区分・障害配偶者カウント・
//   居住開始区分名・所得金額調整控除の同配/調整摘要・前社/中途就職）を網羅する。
//   入力欄のみ設定して保存し、金額計算は帳票生成時の ComputeYearEnd に委ねる。
// ============================================================
string SeedYearEnd(int number, int i, Employee emp)
{
    var ye = yeaRepo.LoadOrCreate(number);   // 給与賞与トランの年間集計込みでロード
    ye.乙欄 = "1";                            // 年末調整する
    ye.源泉徴収票提出 = true;

    // 判定年で「ちょうど 年齢 歳前後」になる生年月日（6/15＝1/1・1/2境界の内側）。
    string Born(int 年齢) => $"{判定年 - 年齢}/06/15";

    void 扶(int idx, string 氏名, string 生年月日, string 障害 = "3", bool 同居 = true,
           bool 非居住 = false, string 留学送金 = "0", string 収入種類 = "", int 収入 = 0)
    {
        var f = ye.扶養親族[idx];
        f.氏名 = 氏名; f.フリガナ = "テスト フヨウ"; f.生年月日 = 生年月日; f.続柄 = "子";
        f.障害 = 障害; f.同居 = 同居; f.非居住者 = 非居住; f.留学送金 = 留学送金;
        f.収入種類 = 収入種類; f.収入 = 収入;
    }

    string sc;
    switch (i)
    {
        case 1:  // 高給＋障害配偶者（特別・同居）＋所得金額調整控除 → （同配）／（調整）＋障害者数カウント
            ye.配偶者.氏名 = "テスト 配偶"; ye.配偶者.フリガナ = "テスト ハイグウ";
            ye.配偶者.生年月日 = Born(50); ye.配偶者.障害 = "2"; ye.配偶者.同居 = true;
            ye.配偶者.収入種類 = "4"; ye.配偶者.収入 = "300000";   // 合計所得30万（≦58万）
            扶(0, "テスト 子1", Born(20), 障害: "2", 収入種類: "4", 収入: 0); // 23歳未満・特別障害 → （調整）＋障害特別内
            ye.所得金額調整控除適用 = true;
            ye.所得金額調整控除適用の扶養者氏名 = "テスト 子2";       // → （調整）
            sc = "高給＋障害配偶者(特別同居)＋所得金額調整控除＝（同配）（調整）＋障害者数カウント";
            break;

        case 2:  // 特定親族（19-22・所得60万）＋老人扶養＋16歳未満＋配偶者特別控除
            扶(0, "テスト 特定", Born(21), 同居: true, 収入種類: "4", 収入: 600000); // 特定親族控除63万 → 区分"10"
            扶(1, "テスト 老親", Born(75), 同居: true);                               // 老人（内）
            扶(2, "テスト 年少", Born(10), 同居: true);                               // 16歳未満
            ye.配偶者.氏名 = "テスト 配偶2"; ye.配偶者.フリガナ = "テスト ハイグウ";
            ye.配偶者.生年月日 = Born(68); ye.配偶者.収入種類 = "4"; ye.配偶者.収入 = "550000"; // 配偶者特別控除
            sc = "特定親族区分10＋老人内＋16歳未満＋控除対象配偶者(特別控除)";
            break;

        case 3:  // 非居住者の詳細区分（01-04）＋16歳未満非居住＋5人目以降（摘要へ区分）
            扶(0, "テスト 非1", Born(25), 非居住: true);                        // 16-30歳 → "01"
            扶(1, "テスト 非2", Born(45), 非居住: true, 留学送金: "1");         // 留学 → "02"
            扶(2, "テスト 非3", Born(45), 非居住: true, 障害: "1");             // 障害 → "03"
            扶(3, "テスト 非4", Born(45), 非居住: true, 留学送金: "2");         // 38万以上送金 → "04"
            扶(4, "テスト 非幼", Born(10), 非居住: true);                       // 16歳未満・非居住 → 区分○
            扶(5, "テスト 非5", Born(45), 非居住: true, 留学送金: "2");         // 5人目(16以上) → 備考＋摘要（04）
            sc = "非居住者区分01-04＋16歳未満非居住＋5人目以降(備考/摘要区分)";
            break;

        case 4:  // 住宅借入金等特別控除（新築購入＋増改築の2回）＋生保・地震
            ye.住宅控除 = 100000; ye.住宅借入金特別控除可能額 = 150000;
            ye.新築購入居住開始 = "2024/04/01"; ye.新築購入適用区分 = "2"; ye.新築購入借入金 = 20000000; // 区分名"住"
            ye.増改築居住開始 = "2025/03/15"; ye.増改築適用区分 = "4"; ye.増改築借入金 = 5000000;       // 区分名"増"
            ye.新生命保険 = 80000; ye.介護医療保険 = 40000; ye.地震保険 = 50000;
            sc = "住宅借入金(新築購入＋増改築・居住開始区分名)＋生保・地震内訳";
            break;

        case 5:  // 前社収入＋中途就職＋摘要追記
            ye.就職 = true; ye.中途就職日 = "2026/05/01";
            ye.前社の名称 = "前職テスト株式会社"; ye.所在地 = "大阪府大阪市北区1-1";
            ye.前社収入 = 1200000; ye.前社源泉 = 30000; ye.前社社保 = 180000; ye.前社退職日 = "2026/04/20";
            ye.源泉徴収票摘要欄追記文 = "追記テスト文言（摘要欄）";
            sc = "前社収入＋中途就職＋摘要追記文";
            break;

        default: // 6人目以降：甲欄・扶養なしの基本形
            sc = "基本形（甲欄・配偶者/扶養なし）";
            break;
    }

    yeaRepo.Save(ye);
    return sc;
}

// ---- 年末調整（T_年末調整）への動的INSERT ----
void WriteYearEnd(string number, Dictionary<int, BonusEntry> 賞与結果)
{
    var cols = TableColumns("T_年末調整");
    if (cols.Count == 0) return;

    var names = new List<string>();
    var vals = new List<object?>();
    foreach (var (name, type) in cols)
    {
        names.Add(name);
        vals.Add(YearEndValue(name, type, number, 賞与結果));
    }

    using var cmd = conn.CreateCommand();
    string colList = string.Join(", ", names.Select(n => $"\"{n}\""));
    string parList = string.Join(", ", names.Select((_, i) => $"$p{i}"));
    cmd.CommandText = $"INSERT OR REPLACE INTO \"T_年末調整\" ({colList}) VALUES ({parList});";
    for (int i = 0; i < vals.Count; i++)
        cmd.Parameters.AddWithValue($"$p{i}", vals[i] ?? DBNull.Value);
    cmd.ExecuteNonQuery();
}

object? YearEndValue(string name, string type, string number, Dictionary<int, BonusEntry> 賞与結果)
{
    if (name.Equals("Number", StringComparison.OrdinalIgnoreCase)) return number;
    if (name is "年度" or "処理年" or "対象年") return payday.支給年;

    // 賞与処理が更新する累計列（賞与N／社保賞与N／源泉賞与N／賞与扶養親族数N）を実値で埋める。
    for (int n = 1; n <= 4; n++)
    {
        賞与結果.TryGetValue(n, out var b);
        if (name == $"賞与{n}") return b?.総支給額 ?? 0;
        if (name == $"社保賞与{n}") return b?.社会保険合計 ?? 0;
        if (name == $"源泉賞与{n}") return b?.所得税 ?? 0;
        if (name == $"賞与扶養親族数{n}") return b?.扶養親族数 ?? 0;
    }

    // それ以外は型に応じた既定値（NOT NULL対策）。
    string t = type.ToUpperInvariant();
    bool 数値 = t.Contains("INT") || t.Contains("REAL") || t.Contains("NUM") || t.Contains("DEC") || t.Contains("DOUB") || t.Contains("FLOA");
    return 数値 ? 0 : "";
}

// ============================================================
// クリーンアップ
// ============================================================
void Clean()
{
    var numbers = new List<string>();
    using (var sel = conn.CreateCommand())
    {
        sel.CommandText = "SELECT Number FROM T_従業員情報 WHERE No LIKE $p";
        sel.Parameters.AddWithValue("$p", prefix + "%");
        using var r = sel.ExecuteReader();
        while (r.Read()) numbers.Add(r.GetString(0));
    }
    if (numbers.Count == 0) { Console.WriteLine($"接頭辞 '{prefix}' のテスト従業員は見つかりませんでした。"); return; }

    int del = 0;
    foreach (var num in numbers)
    {
        del += Delete("T_給与トラン", num);
        del += Delete("T_賞与トラン", num);
        if (TableExists("T_年末調整")) del += Delete("T_年末調整", num);
        del += Delete("T_従業員情報", num);
    }
    Console.WriteLine($"テスト従業員 {numbers.Count} 人 と関連 {del} 行を削除しました（接頭辞 '{prefix}'）。");
}

int Delete(string table, string number)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"DELETE FROM \"{table}\" WHERE Number = $n";
    cmd.Parameters.AddWithValue("$n", number);
    return cmd.ExecuteNonQuery();
}

// ============================================================
// スキーマ補助
// ============================================================
// レガシーDB（SQLCipher）を復号し、T_年末調整 の CREATE 文を取り出して開発DBに作る。
bool TryCreateYearEndTableFromLegacy()
{
    string legacyPath = Arg("--legacy")
        ?? @"C:\Users\kawab\source\repos\syuhousystem\PayCalcDX2026\PayCalcDX\DB\PayCalc2026.db";
    if (!File.Exists(legacyPath))
    {
        Console.WriteLine($"⚠ レガシーDBが見つかりません（{legacyPath}）。--legacy で指定できます。");
        return false;
    }
    try
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"PayCalc_yea_{Guid.NewGuid():N}.db");
        LegacyDbDecryptor.DecryptFile(legacyPath, tmp);
        string? ddl;
        using (var lc = new SqliteConnection($"Data Source={tmp}"))
        {
            lc.Open();
            using var q = lc.CreateCommand();
            q.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='T_年末調整';";
            ddl = q.ExecuteScalar() as string;
        }
        try { File.Delete(tmp); } catch { }
        if (string.IsNullOrWhiteSpace(ddl)) { Console.WriteLine("⚠ レガシーDBにも T_年末調整 がありません。"); return false; }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
        Console.WriteLine("→ T_年末調整 をレガシーDBのスキーマで作成しました。\n");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠ T_年末調整 の作成に失敗：{ex.Message}");
        return false;
    }
}

bool TableExists(string name)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
    cmd.Parameters.AddWithValue("$n", name);
    return cmd.ExecuteScalar() is not null;
}

List<(string name, string type)> TableColumns(string table)
{
    var list = new List<(string, string)>();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
    using var r = cmd.ExecuteReader();
    while (r.Read())
        list.Add((r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2)));
    return list;
}
