using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Services;
using PayCalcDXPro.Web;
using PayCalcDXPro.Web.Components;

// 配布版（self-contained）はどこから起動しても appsettings/wwwroot を確実に読むため
// コンテントルートをexeの場所に固定する。ただし開発実行（dotnet run）でこれを固定すると
// 静的アセット（wwwroot / _content）が配信されなくなるため、開発時は既定（プロジェクト）に任せる。
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = isDevelopment ? null : AppContext.BaseDirectory,
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton<LocalDatabaseService>();
builder.Services.AddSingleton<TimeCardImportService>();
// タブUIの選択状態を回線（セッション）単位で保持する（画面遷移・従業員切替をまたいで復元）。
builder.Services.AddScoped<TabStateService>();

// ログイン（Cookie認証）。セキュリティアカウントが登録されている場合のみゲートを有効化する（現行 F_データの初期化 の設定/解除に対応）。
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "PayCalcDXPro.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// 体験版の年度ロック（現行デスクトップ版 taikenbanCheck の移植）。
// 初回起動で現在年度(Trial:Year)のマーカーを作成し、別年度の形跡・改ざんはブロックする。製品版は Trial:Enabled=false。
int trialYear = builder.Configuration.GetValue("Trial:Year", 2026);
bool trialEnabled = builder.Configuration.GetValue("Trial:Enabled", true);
builder.Services.AddSingleton(new PayCalcDXPro.Security.TrialGuard(trialEnabled, trialYear));

// スタンドアロン（ローカルPC）起動：固定URLで待ち受け、起動後に既定ブラウザを開く。
bool standalone = builder.Configuration.GetValue("PayCalc:Standalone", false)
                  || Environment.GetEnvironmentVariable("PAYCALC_STANDALONE") == "1";
string url = builder.Configuration["PayCalc:Url"] ?? "http://localhost:5080";
if (standalone)
{
    builder.WebHost.UseUrls(url);
    // ブラウザ／タブを閉じたらサーバを自動終了する（スタンドアロンのみ）。
    builder.Services.AddSingleton<PayCalcDXPro.Web.StandaloneLifetime>();
    builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler,
        PayCalcDXPro.Web.ShutdownCircuitHandler>();
}

var app = builder.Build();

// アプリ終了時の自動バックアップ（現行の終了時自動バックアップ相当）。設定がオンのときだけ実行する。
app.Lifetime.ApplicationStopping.Register(() =>
{
    try { app.Services.GetRequiredService<LocalDatabaseService>().RunAutoBackupIfEnabled(); }
    catch { /* 終了処理を妨げない */ }
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    if (!standalone) app.UseHsts(); // スタンドアロンはローカルhttpのためHSTS不要
}

if (!standalone) app.UseHttpsRedirection(); // ローカルhttpで動かすためリダイレクトしない

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ログインゲート：セキュリティアカウントが登録済みで未認証のアクセスは /login へ誘導する。
// ログイン画面・ログアウト・Blazorフレームワーク・静的アセットは常に許可（無限リダイレクト防止）。
app.Use(async (context, next) =>
{
    var db = context.RequestServices.GetRequiredService<LocalDatabaseService>();

    // 認証済みリクエストでは監査ログの操作者を Cookie の実ユーザーに同期する。
    // サーバー再起動後も Cookie が有効なら、これで操作者が「システム」に戻らず実名で記録される。
    if (context.User.Identity?.IsAuthenticated == true)
        db.CurrentOperator = context.User.ToPrincipalOrNull();

    if (LoginGate.RequiresLogin(context) && db.SecurityAccountExists())
    {
        var ret = context.Request.Path + context.Request.QueryString;
        context.Response.Redirect("/login?returnUrl=" + Uri.EscapeDataString(ret));
        return;
    }
    await next();
});

// ログイン画面（自己完結HTML）。アカウント未登録なら本来ゲート不要なのでトップへ。
app.MapGet("/login", (LocalDatabaseService db, string? returnUrl) =>
    db.SecurityAccountExists()
        ? Results.Content(LoginGate.BuildLoginHtml(returnUrl), "text/html; charset=utf-8")
        : Results.Redirect("/"));

// ログイン実行：ID/パスワードを検証し、操作者名（必須）を採用してCookieを発行、戻り先へ。
// アカウントは権限者間で共有するため、監査ログの操作者はアカウントの設定者名ではなく、ここで入力された操作者名を使う
// （現行 F_ログイン：txt_name→AccountInfo.Name→AccessLog）。失敗は入力値を保持してエラー再表示。
app.MapPost("/login", async (HttpContext http, LocalDatabaseService db) =>
{
    var form = await http.Request.ReadFormAsync();
    string id = form["id"].ToString();
    string password = form["password"].ToString();
    string operatorName = form["operatorName"].ToString();
    string returnUrl = form["returnUrl"].ToString();
    string back = (!string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/')) ? returnUrl : "/";

    // 操作者名は必須（現行 MN001E：アカウント名未入力）。
    if (string.IsNullOrWhiteSpace(operatorName))
        return Results.Content(
            LoginGate.BuildLoginHtml(back, "操作者名を入力してください。", id, operatorName),
            "text/html; charset=utf-8");

    var principal = db.AuthenticateSecurityAccount(id, password);
    if (principal is null)
        return Results.Content(
            LoginGate.BuildLoginHtml(back, "アカウントIDまたはパスワードが正しくありません。", id, operatorName),
            "text/html; charset=utf-8");

    // 操作者名＝ログインで入力された実際の操作者（設定者名 principal.Name ではなくこちらを採用）。
    string op = operatorName.Trim();
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, principal.Id),
        new(ClaimTypes.Name, op),
    };
    foreach (var role in principal.Roles) claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    // 監査ログの操作者を、ログインした実際の操作者に切り替える（アカウントID・ロールは維持、氏名は操作者名）。
    db.CurrentOperator = principal with { Name = op };

    return Results.Redirect(back);
}).DisableAntiforgery();

// ログアウト：Cookieを破棄してログイン画面へ。
app.MapGet("/logout", async (HttpContext http, LocalDatabaseService db) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    db.CurrentOperator = null;   // 操作者をシステム扱いに戻す（次のログインまで）。
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 支払明細書PDF。inline で返しブラウザのPDFビューア（印刷/保存可）でプレビューする。
// kind=salary|bonus, round=支給回/賞与回, two=A4に2明細, nums=対象従業員Number(カンマ区切り。空＝保存済み全員)、
// empty=賃金が発生していない従業員も空明細で含める。
app.MapGet("/payroll/payslip/pdf", (LocalDatabaseService db, string? kind, int round, bool? two, string? nums, bool? empty) =>
{
    var numbers = (nums ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
        .Where(n => n is not null).Select(n => n!.Value).ToList();
    var bytes = kind == "bonus"
        ? db.BuildBonusPayslips(round, numbers, two ?? false, empty ?? false)
        : db.BuildPayrollPayslips(round, numbers, two ?? false, empty ?? false);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 導入設定の台帳印刷。kind=setup（導入設定台帳）/ employee（従業員台帳。nums=対象Number。空＝全員）。
app.MapGet("/setup/ledger/pdf", (LocalDatabaseService db, string? kind, string? nums) =>
{
    byte[] bytes;
    if (kind == "employee")
    {
        var numbers = (nums ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
            .Where(n => n is not null).Select(n => n!.Value).ToList();
        bytes = db.BuildEmployeeLedgerPdf(numbers);
    }
    else
    {
        bytes = db.BuildSetupLedgerPdf();
    }
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 賃金台帳（支給日別）。kind=salary（給与）/ bonus（賞与）。round=支給回/賞与回。dept=部門名（空＝全部門）。empty=賃金なしも含める。
app.MapGet("/payroll/wage-ledger/pdf", (LocalDatabaseService db, string? kind, int round, string? dept, bool? empty) =>
{
    string? 部門 = string.IsNullOrWhiteSpace(dept) ? null : dept;
    var bytes = kind == "bonus"
        ? db.BuildBonusLedgerPdf(round, 部門, empty ?? false)
        : db.BuildWageLedgerPdf(round, 部門, empty ?? false);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 賃金台帳（従業員別）。kind=salary（給与）/ bonus（賞与）。dept=部門名（空＝全部門）。empty=賃金なしも含める。
app.MapGet("/payroll/wage-ledger-emp/pdf", (LocalDatabaseService db, string? kind, string? dept, bool? empty) =>
{
    string? 部門 = string.IsNullOrWhiteSpace(dept) ? null : dept;
    var bytes = db.BuildWageLedgerEmpPdf(kind == "bonus", 部門, empty ?? false);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 関連帳票：銀行振込一覧表。kind=salary（給与）/ bonus（賞与）。round=処理回。
app.MapGet("/payroll/bank-transfer/pdf", (LocalDatabaseService db, string? kind, int round) =>
{
    var bytes = db.BuildBankTransferPdf(kind == "bonus", round);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 関連帳票：現金支給金種表。kind=salary/bonus。round=処理回。yen2000=2,000円札を含める。
app.MapGet("/payroll/kinsyu/pdf", (LocalDatabaseService db, string? kind, int round, bool? yen2000) =>
{
    var bytes = db.BuildKinsyuPdf(kind == "bonus", round, yen2000 ?? false);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 関連帳票：給与/賞与封筒印刷。kind=salary/bonus。round=処理回。landscape=横長封筒。empty=賃金なしも含める。nums=対象Number（空＝当回保存済み全員）。
app.MapGet("/payroll/envelope/pdf", (LocalDatabaseService db, string? kind, int round, bool? landscape, bool? empty, string? nums) =>
{
    var numbers = (nums ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
        .Where(n => n is not null).Select(n => n!.Value).ToList();
    var bytes = db.BuildEnvelopePdf(kind == "bonus", round, landscape ?? false, empty ?? false, numbers);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整：退職所得の源泉徴収票・特別徴収票（現行 R_退職源泉）。no=整理番号（1件＝4面付け1ページ）。
app.MapGet("/year-end/retirement/pdf", (LocalDatabaseService db, int no) =>
{
    var bytes = db.BuildRetirementWithholdingPdf(no);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整：報酬、料金、契約金及び賞金の支払調書（現行 R_報酬）。no=整理番号（1件＝4面付け1ページ）。
app.MapGet("/year-end/reward/pdf", (LocalDatabaseService db, int no) =>
{
    var bytes = db.BuildRewardPdf(no);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整：不動産の使用料等の支払調書（現行 R_不動産）。no=整理番号（1件＝4面付け1ページ）。
app.MapGet("/year-end/realestate/pdf", (LocalDatabaseService db, int no) =>
{
    var bytes = db.BuildRealEstatePdf(no);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整：不動産等の譲受けの対価の支払調書（現行 R_不動産譲受）。no=整理番号（1件＝4面付け1ページ）。
app.MapGet("/year-end/realestate-transfer/pdf", (LocalDatabaseService db, int no) =>
{
    var bytes = db.BuildRealEstateTransferPdf(no);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整：不動産等の売買又は貸付けのあっせん手数料の支払調書（現行 R_不動産あっせん）。no=整理番号（1件＝4面付け1ページ）。
app.MapGet("/year-end/realestate-mediation/pdf", (LocalDatabaseService db, int no) =>
{
    var bytes = db.BuildRealEstateMediationPdf(no);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整：給与所得の源泉徴収票（現行 R_源泉徴収票）。numbers=カンマ区切りの従業員Number（複数可・全員を1つの複数ページPDFに）。
// 後方互換で number（単一）も受け付ける。種別/元号/年は任意（既定あり）。
app.MapGet("/year-end/withholding-slip/pdf", (LocalDatabaseService db, int? number, string? numbers,
    string? 種別, string? 元号, string? 年) =>
{
    int[] nums = (numbers ?? number?.ToString() ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var n) ? n : -1)
        .Where(n => n > 0)
        .ToArray();
    var bytes = db.BuildWithholdingSlipPdf(nums, 種別 ?? "給料・賞与", 元号 ?? "令和", 年 ?? "");
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整：給与支払報告書（個人別明細書）（現行 R_給与支払報告書）。numbers=カンマ区切り。種別/年は任意。
app.MapGet("/year-end/pay-report/pdf", (LocalDatabaseService db, int? number, string? numbers,
    string? 種別, string? 年) =>
{
    int[] nums = (numbers ?? number?.ToString() ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var n) ? n : -1)
        .Where(n => n > 0)
        .ToArray();
    var bytes = db.BuildPayReportPdf(nums, 種別 ?? "給料・賞与", 年 ?? "");
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整：給与所得に対する源泉徴収簿（現行 R_徴収簿）。numbers=カンマ区切り（複数可・全員を1つの複数ページPDFに）。元号/年は任意。
app.MapGet("/year-end/withholding-ledger/pdf", (LocalDatabaseService db, int? number, string? numbers,
    string? 元号, string? 年) =>
{
    int[] nums = (numbers ?? number?.ToString() ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var n) ? n : -1)
        .Where(n => n > 0)
        .ToArray();
    var bytes = db.BuildWithholdingLedgerPdf(nums, 元号 ?? "令和", 年 ?? "");
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 年末調整 集計表（納付書用）。syukei=1 納期特例(7〜12月)/2 一般(12月のみ)、syouyo=1〜5（含む賞与の範囲）。
app.MapGet("/year-end/summary/pdf", (LocalDatabaseService db, int? syukei, int? syouyo) =>
{
    var bytes = db.BuildSummaryPdf(syukei ?? 1, syouyo ?? 1);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 給与所得の源泉徴収票等の法定調書合計表（現行 R_合計表）。全社集計のため引数なし。
app.MapGet("/year-end/legal-record/pdf", (LocalDatabaseService db) =>
{
    var bytes = db.BuildLegalRecordSummaryPdf();
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 給与支払報告書（総括表）（現行 R_総括表）。全社集計のため引数なし（提出先最大4件＝4面付け1ページ）。
app.MapGet("/year-end/pay-report-summary/pdf", (LocalDatabaseService db) =>
{
    var bytes = db.BuildPayReportSummaryPdf();
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf");
});

// 法定調書の印刷ハブ（現行 F_法定調書印刷）。reports=カンマ区切りの帳票キー
// （soukatu/taishoku/houshu/fudousan/jouto/assen/goukei）、teishutu=1 提出分のみ／2 全て。
// 選択帳票を1つの複数ページPDFに結合して返す。
app.MapGet("/year-end/legal-documents/pdf", (LocalDatabaseService db, string? reports, int? teishutu) =>
{
    string[] keys = (reports ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    int kbn = teishutu ?? 2;
    var bytes = db.BuildLegalDocumentsPdf(keys, kbn);
    if (bytes.Length > 0) return Results.File(bytes, "application/pdf");

    // 選択帳票に印刷対象が1件も無い場合（提出分のみで提出対象が未登録 等）は、
    // 404（ページが見つかりません）ではなく、プレビュー/別タブで読める案内を返す。
    string 注記 = kbn == 1
        ? "「提出対象区分」が『提出分のみ印刷』のため、提出対象に設定された調書だけが対象です。"
        : "選択した帳票に印刷できる登録データがありません。";
    string html = $$"""
        <!doctype html><html lang="ja"><head><meta charset="utf-8">
        <title>印刷対象なし</title>
        <style>body{font-family:'Meiryo','Yu Gothic',sans-serif;margin:0;padding:48px;color:#333;background:#fafafa}
        .card{max-width:640px;margin:0 auto;background:#fff;border:1px solid #e0e0e0;border-radius:10px;padding:28px 32px}
        h1{font-size:1.15rem;margin:0 0 12px}p{line-height:1.8;margin:.4em 0}.hint{color:#666;font-size:.9rem}</style>
        </head><body><div class="card">
        <h1>印刷対象のデータがありません</h1>
        <p>{{注記}}</p>
        <p class="hint">すべての登録を印刷する場合は「提出対象区分」を『全て印刷』に切り替えてください。
        提出対象だけを印刷する場合は、各支払調書の入力画面で「提出（不動産系は提出対象）」を設定してください。</p>
        </div></body></html>
        """;
    return Results.Content(html, "text/html; charset=utf-8");
});

// アクセスログ（操作ログ）の CSV 出力。現行 F_アクセスログ参照出力 のログ出力に相当。
app.MapGet("/setup/audit-log/csv", (LocalDatabaseService db) =>
{
    // セキュリティアカウント未設定時はマイナンバー管理の操作不可（現行 DN006E）。直URLでのログ取得も塞ぐ。
    if (!db.SecurityAccountExists()) return Results.Forbid();

    var bytes = db.BuildAuditLogCsv();
    return bytes.Length == 0
        ? Results.NotFound()
        : Results.File(bytes, "text/csv", $"LogData{DateTime.Now:yyyyMMddHHmmss}.csv");
});

// 会社情報（基本情報）の CSV 出力。ログインゲート配下（未認証は /login へ誘導）。
app.MapGet("/setup/company/csv", (LocalDatabaseService db) =>
{
    var bytes = db.BuildCompanyCsv();
    return bytes.Length == 0
        ? Results.NotFound()
        : Results.File(bytes, "text/csv", $"会社情報_{DateTime.Now:yyyyMMddHHmmss}.csv");
});

// 従業員（基本情報）の CSV 出力。nums＝カンマ区切りの従業員Number（未指定なら全員）。
app.MapGet("/setup/employees/csv", (LocalDatabaseService db, string? nums) =>
{
    var numbers = (nums ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
        .Where(n => n is not null).Select(n => n!.Value).ToList();
    var bytes = db.BuildEmployeesCsv(numbers);
    return bytes.Length == 0
        ? Results.NotFound()
        : Results.File(bytes, "text/csv", $"従業員情報_{DateTime.Now:yyyyMMddHHmmss}.csv");
});

if (standalone)
{
    // サーバ起動完了後に既定ブラウザでアプリを開く。
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ブラウザ起動失敗時は手動アクセス */ }
    });
}

app.Run();
