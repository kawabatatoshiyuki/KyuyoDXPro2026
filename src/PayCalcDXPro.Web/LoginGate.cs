using System.Net;
using System.Security.Claims;
using PayCalcDXPro.Security;

namespace PayCalcDXPro.Web;

/// <summary>
/// ログインゲートの判定と、自己完結型のログイン画面HTMLを提供する。
/// セキュリティアカウントが登録されている場合のみ、未認証アクセスを /login へ誘導する（Program.cs のミドルウェアから使用）。
/// </summary>
public static class LoginGate
{
    // 認証不要で常に通すパス（ログイン/ログアウト、Blazorフレームワーク・SignalR、静的コンテンツ、エラー）。
    private static readonly string[] _openPrefixes =
    {
        "/login", "/logout", "/_blazor", "/_framework", "/_content", "/Error",
    };

    /// <summary>
    /// ログイン Cookie の Claims から操作者 <see cref="Principal"/> を復元する（認証済みでなければ null）。
    /// アカウントID＝NameIdentifier、氏名＝Name、ロール＝Role クレーム（ログイン時に付与）。
    /// サーバー再起動後も Cookie が有効なら、これで監査ログの操作者を実ユーザーに復元できる。
    /// </summary>
    public static Principal? ToPrincipalOrNull(this ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;

        string id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        string name = user.FindFirst(ClaimTypes.Name)?.Value ?? id;
        var roles = new HashSet<Role>();
        foreach (var c in user.FindAll(ClaimTypes.Role))
            if (Enum.TryParse<Role>(c.Value, out var r)) roles.Add(r);

        return new Principal(id, name, roles);
    }

    /// <summary>このリクエストにログインを要求すべきか（認証済み・許可パス・静的アセットは false）。</summary>
    public static bool RequiresLogin(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true) return false;

        string path = ctx.Request.Path.Value ?? "/";
        foreach (var open in _openPrefixes)
            if (path.StartsWith(open, StringComparison.OrdinalIgnoreCase)) return false;

        // 拡張子付き（/app.css, /favicon.ico, /js/app-input.js 等）は静的アセットとして許可する。
        int slash = path.LastIndexOf('/');
        if (path.AsSpan(slash + 1).Contains('.')) return false;

        return true;
    }

    /// <summary>
    /// ログイン画面のHTML（MudBlazorに依存しない自己完結ページ）。
    /// アカウントは権限者間で共有するため、操作者名を毎回入力させ、アクセスログの操作者として記録する
    /// （現行 F_ログイン の txt_name＝AccountInfo.Name 相当）。エラー時は入力値（ID・操作者名）を保持して再表示する。
    /// </summary>
    public static string BuildLoginHtml(string? returnUrl, string? errorMessage = null, string id = "", string operatorName = "")
    {
        string ret = WebUtility.HtmlEncode(returnUrl ?? "/");
        string err = string.IsNullOrEmpty(errorMessage)
            ? ""
            : $"<div class=\"l-err\">{WebUtility.HtmlEncode(errorMessage)}</div>";
        string idVal = WebUtility.HtmlEncode(id);
        string opVal = WebUtility.HtmlEncode(operatorName);

        return $$"""
        <!DOCTYPE html>
        <html lang="ja">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>ログイン｜給与計算DXPro</title>
            <link rel="preconnect" href="https://fonts.googleapis.com" />
            <link href="https://fonts.googleapis.com/css2?family=Noto+Sans+JP:wght@400;500;700&display=swap" rel="stylesheet" />
            <style>
                /* 勤怠管理DXPro のログイン画面デザインに寄せた白基調カード＋紫グラデ背景。 */
                * { box-sizing: border-box; }
                body { margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center; padding:1.5rem;
                       background:linear-gradient(135deg,#667eea,#764ba2);
                       font-family:'Noto Sans JP',-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif; color:#18181b; }
                .lc { background:#fff; border-radius:20px; padding:2.5rem 2rem; width:100%; max-width:400px;
                      box-shadow:0 10px 15px rgba(0,0,0,.08),0 4px 6px rgba(0,0,0,.05); }
                .l-logo { text-align:center; margin-bottom:2rem; }
                .l-icon { width:64px; height:64px; background:#fff; border-radius:16px; display:inline-flex;
                          align-items:center; justify-content:center; margin-bottom:12px;
                          box-shadow:0 4px 14px rgba(0,0,0,.12); overflow:hidden; }
                .l-icon img { width:48px; height:48px; display:block; object-fit:contain; }
                .l-logo h1 { margin:0; font-size:22px; font-weight:700; }
                .l-logo p { margin:4px 0 0; font-size:13px; color:#71717a; }
                .fg { margin-bottom:1rem; }
                .fl { display:block; font-size:12px; font-weight:600; color:#71717a; margin-bottom:6px; letter-spacing:.04em; }
                .fi { width:100%; padding:11px 14px; border:1.5px solid #e8e8ec; border-radius:8px; font-size:15px;
                      color:#18181b; background:#f9f9fb; font-family:inherit;
                      transition:border-color .15s,box-shadow .15s; -webkit-appearance:none; }
                .fi:focus { outline:none; border-color:#6366f1; box-shadow:0 0 0 3px rgba(99,102,241,.15); background:#fff; }
                .l-btn { width:100%; padding:13px; background:#6366f1; color:#fff; border:none; border-radius:8px;
                         font-size:15px; font-weight:600; cursor:pointer; margin-top:.5rem; font-family:inherit;
                         box-shadow:0 2px 8px rgba(99,102,241,.35); transition:background .15s,transform .1s; }
                .l-btn:hover { background:#4f46e5; }
                .l-btn:active { transform:scale(.98); }
                .l-err { background:#fef2f2; color:#991b1b; border:1px solid #fecaca; border-radius:6px;
                         padding:10px 14px; font-size:13px; margin-bottom:1rem; }
                .l-hint { font-size:12px; color:#a1a1aa; margin:8px 0 0; line-height:1.5; }
                .l-foot { text-align:center; font-size:12px; color:#a1a1aa; margin-top:1.5rem; }
            </style>
        </head>
        <body>
            <div class="lc">
                <div class="l-logo">
                    <div class="l-icon"><img src="/img/DXPro2026.png" alt="給与計算DXPro" /></div>
                    <h1>給与計算DXPro</h1>
                    <p>アカウントにサインイン</p>
                </div>
                <form method="post" action="/login">
                    <input type="hidden" name="returnUrl" value="{{ret}}" />
                    {{err}}
                    <div class="fg">
                        <label class="fl" for="id">アカウントID</label>
                        <input class="fi" id="id" name="id" type="text" autocomplete="username" value="{{idVal}}" autofocus required />
                    </div>
                    <div class="fg">
                        <label class="fl" for="password">パスワード</label>
                        <input class="fi" id="password" name="password" type="password" autocomplete="current-password" required />
                    </div>
                    <div class="fg">
                        <label class="fl" for="operatorName">操作者名</label>
                        <input class="fi" id="operatorName" name="operatorName" type="text" autocomplete="off" value="{{opVal}}" maxlength="64" required />
                        <p class="l-hint">※ 入力した操作者名がアクセスログに記録されます（誰が操作したかの記録用）。</p>
                    </div>
                    <button class="l-btn" type="submit">ログイン</button>
                </form>
                <div class="l-foot">給与計算DXPro ブラウザ版</div>
            </div>
        </body>
        </html>
        """;
    }
}
