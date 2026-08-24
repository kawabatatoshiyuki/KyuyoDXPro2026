using System.Globalization;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 西暦→和暦の変換（現行 MFunction.adToJapCal 相当）。.NET の JapaneseCalendar を用い、
/// 元号データはランタイム同梱（外部依存なし）。法定調書等の「年」欄に使う和暦年数を返す。
/// </summary>
public static class Wareki
{
    private static readonly JapaneseCalendar Cal = new();
    private static readonly CultureInfo Ja = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>和暦の年数のみ（例：2025→「7」）。現行 adToJapCal(FORMAT_1) に一致（先頭0は除去）。</summary>
    public static string 年数(int 西暦年)
    {
        // 年央で判定し元号境界（年始・年末）の跨ぎを避ける。
        int era年 = Cal.GetYear(new DateTime(西暦年, 7, 1));
        return era年.ToString();
    }

    /// <summary>和暦の「年.月.日」（例：2024/4/1→「6.4.1」）。現行 adToJapCal(FORMAT_2) に一致（年の先頭0は除去）。</summary>
    public static string 年月日(DateTime 西暦日)
    {
        var jaCal = (CultureInfo)Ja.Clone();
        jaCal.DateTimeFormat.Calendar = Cal;
        string s = 西暦日.ToString("yy.M.d", jaCal);
        if (s.StartsWith("0", StringComparison.Ordinal)) s = s.Substring(1); // 元年〜9年の先頭0を除去
        return s;
    }

    /// <summary>元号名（例：2025→「令和」）。様式に元号が無い帳票向け。</summary>
    public static string 元号(int 西暦年)
    {
        var jaEra = (CultureInfo)Ja.Clone();
        jaEra.DateTimeFormat.Calendar = Cal;
        return new DateTime(西暦年, 7, 1).ToString("ggg", jaEra);
    }
}
