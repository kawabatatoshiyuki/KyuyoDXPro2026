using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;

namespace PayCalcDXPro.Web;

/// <summary>
/// 利用者向けエラー通知のヘルパー（情報漏洩対策）。
/// 例外の詳細（メッセージ・スタックトレース）はサーバーログにのみ記録し、
/// 画面には内部情報（例外メッセージ・ファイルパス・SQL 等）を含まない汎用メッセージだけを表示する。
/// </summary>
public static class AppErrors
{
    private static ILogger _logger = NullLogger.Instance;

    /// <summary>起動時に ILoggerFactory を渡してロガーを初期化する（Program.cs から呼ぶ）。</summary>
    public static void Configure(ILoggerFactory factory) =>
        _logger = factory.CreateLogger("PayCalcDXPro");

    /// <summary>例外の詳細をサーバーログにのみ記録する（画面には出さない）。</summary>
    public static void Log(Exception ex, string context)
    {
        try { _logger.LogError(ex, "{Context}", context); }
        catch { /* ログ失敗で業務処理を妨げない */ }
    }

    /// <summary>
    /// 例外詳細をサーバーログに記録し、Snackbar には汎用メッセージのみ表示する。
    /// <paramref name="userMessage"/> には内部情報（例外メッセージ・パス・SQL 等）を含めないこと。
    /// </summary>
    public static void NotifyError(this ISnackbar snackbar, string userMessage, Exception ex)
    {
        Log(ex, userMessage);
        snackbar.Add(userMessage, Severity.Error);
    }
}
