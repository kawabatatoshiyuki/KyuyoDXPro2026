using Microsoft.Extensions.Configuration;
using PayCalcDXPro.Web;

// タイムカード取込を実DBに対して検証する。UIボタンが呼ぶ3メソッドをそのまま実行する。
// 実行前に Web アプリを停止しておくこと（SecureDataStore の排他のため）。

const string TimeCardDir = @"C:\PayCalcDXPro\TmCalcDATA";

var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
var db = new LocalDatabaseService(cfg);
var tc = new TimeCardImportService(db);   // タイムカード取込は共通モジュールへ分離済み
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"DataDir = {db.DataDir}");

// 1) 取込設定フォルダを TmCalcDATA に設定（未設定/別フォルダでも確実に検出させる）。
var save = tc.SaveTimeCardSetting(TimeCardDir);
Console.WriteLine($"[設定保存] Ok={save.Ok} {save.Message}");

// 2) 2026/6 に対応する支給回を、支給回候補から特定する。
var meta0 = db.LoadPayrollMeta(1);
var 対象 = meta0.支給回候補.FirstOrDefault(r => r.対象年 == 2026 && r.対象月 == 6);
if (対象 is null)
{
    Console.WriteLine("2026/6 に対応する支給回が見つかりません。支給回候補:");
    foreach (var r in meta0.支給回候補)
        Console.WriteLine($"  支給回={r.支給回} 対象={r.対象年}/{r.対象月} 登録済={r.登録済}");
    return;
}
int 支給回 = 対象.支給回;
Console.WriteLine($"対象支給回 = {支給回}（{対象.対象年}/{対象.対象月}）");

// 3) CSV 自動検出。
var path = tc.FindTimeCardCsv(支給回);
Console.WriteLine($"[FindTimeCardCsv] => {path ?? "(null)"}");
if (path is null) { Console.WriteLine("CSVが見つからないため中断。"); return; }

// 4) 解析（DB非変更）。
var plan = tc.AnalyzeTimeCardCsv(path, 支給回);
Console.WriteLine($"[Analyze] Ok={plan.Ok} フォーマット={plan.フォーマット} 対象年月={plan.対象年月} 年月不一致={plan.年月不一致} 行数={plan.行.Count}");
if (!plan.Ok) { Console.WriteLine(plan.Message); return; }

// 反映対象の先頭従業員を BEFORE として記録。
var 先頭 = plan.行.FirstOrDefault(r => !r.未登録);
if (先頭 is null) { Console.WriteLine("反映対象（登録済）行がありません。"); return; }
int number = 先頭.Number!.Value;
var before = db.LoadPayrollEntry(number.ToString(), 支給回);
Console.WriteLine($"\n--- BEFORE (No.{先頭.No} {先頭.氏名} / Number={number}) ---");
Dump(before);
Console.WriteLine($"CSV転記予定: 基本給={先頭.転記?.基本給} 労働時間={先頭.転記?.労働時間} 出勤日数={先頭.転記?.出勤日数} " +
                  $"CSV残業額={先頭.Csv残業金額合計} 再計算残業手当={先頭.再計算時間外手当} 検算差額={先頭.検算差額}");

// 5) 反映（給与トランへ転記→再計算→保存、ログ記録、CSV削除）。
var res = tc.ApplyTimeCardImport(plan);
Console.WriteLine($"\n[Apply] Ok={res.Ok} 反映={res.反映件数} スキップ={res.スキップ件数} : {res.Message}");

// 6) AFTER 読み戻し。
var after = db.LoadPayrollEntry(number.ToString(), 支給回);
Console.WriteLine($"\n--- AFTER (No.{先頭.No} {先頭.氏名}) ---");
Dump(after);

// 7) 取込ログ。
Console.WriteLine("\n--- 取込ログ(最新3件) ---");
foreach (var lg in tc.ListTimeCardImportLogs(3))
    Console.WriteLine($"  {lg.取込日時} {lg.対象年月} {lg.ファイル名} {lg.フォーマット} 反映={lg.反映件数} スキップ={lg.スキップ件数}");

Console.WriteLine($"\nCSV削除確認: {(File.Exists(path) ? "残存(未削除)" : "削除済み(想定どおり)")}");

// 8) 「時間外手当計算」ボタン相当：区分別内訳（端数処理済み）と、その合計＝時間外手当・備考文字列を確認。
Console.WriteLine("\n--- 時間外手当計算(ボタン相当) 区分別内訳 ---");
var parts = db.ComputeOvertimeParts(after, number);
int gokei = 0;
foreach (var (略称, 金額) in parts) { gokei += 金額; if (金額 != 0) Console.WriteLine($"  {略称} = {金額:N0}"); }
Console.WriteLine($"  合計(=時間外手当) = {gokei:N0}");
var 明細 = parts.Where(p => p.金額 != 0).Select(p => $"{p.略称}{p.金額:N0}");
Console.WriteLine($"  備考文字列 = 「{(明細.Any() ? "【時間外内訳】" + string.Join(" ", 明細) : "")}」");

// 9) 乙欄従業員(No.004)の賞与計算が乙欄源泉で通るか（賞与乙欄の疎通確認）。
Console.WriteLine("\n--- 乙欄従業員 賞与計算(No.004) ---");
try
{
    const int otsuNo = 4;
    var bmeta = db.LoadBonusMeta(1);
    int kai = bmeta.賞与回候補.FirstOrDefault(r => r.登録済)?.回 ?? 1;
    var b = db.LoadBonusEntry(otsuNo.ToString(), kai);
    b.賞与 = 500000;
    var bc = db.CalculateBonus(b, otsuNo, kai);
    Console.WriteLine($"  ✓ 回{kai} 賞与500,000 → 課税対象額={bc.課税対象額} 所得税={bc.所得税}");
}
catch (Exception ex) { Console.WriteLine($"  ✗ {ex.GetType().Name}: {ex.Message}"); }

static void Dump(PayCalcDXPro.Data.Payroll.PayrollEntry e)
{
    Console.WriteLine($"  基本給={e.基本給} 労働時間={e.労働時間} 出勤日数={e.出勤日数} 所定出勤日数={e.所定出勤日数}");
    Console.WriteLine($"  法定内残業={e.法定内残業} 普通残業60h以下={e.普通残業60h以下} 深夜残業60h以下={e.深夜残業60h以下} 休日出勤60h以下={e.休日出勤60h以下}");
    Console.WriteLine($"  有給休暇={e.有給休暇} 代休={e.代休} 遅刻早退={e.遅刻早退} 欠勤日数_一般={e.欠勤日数_一般}");
    Console.WriteLine($"  時間外手当={e.時間外手当} 総支給額={e.総支給額} 所得税={e.所得税}");
    Console.WriteLine($"  備考=「{e.備考}」");
}
