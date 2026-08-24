namespace PayCalcDXPro.Domain.Calendar;

/// <summary>
/// 日本の国民の祝日をオフラインで算出する（祝日法ベース）。インターネット不要。
/// 固定日・ハッピーマンデー・春分/秋分（天文近似式）・振替休日・国民の休日に対応。
/// 注意：一度きりの特例（例：2020/2021の海の日・スポーツ・山の日の移動）は再現しない。
/// 春分/秋分の近似式はおおむね2000〜2099年で有効。
/// </summary>
public static class JapaneseHolidays
{
    private static readonly Dictionary<int, HashSet<DateOnly>> _cache = new();

    /// <summary>その日が祝日（振替・国民の休日含む）か。</summary>
    public static bool IsHoliday(DateOnly date) => GetHolidays(date.Year).Contains(date);

    /// <summary>その日が稼働日（土日・祝日でない）か。</summary>
    public static bool IsWorkingDay(DateOnly date)
        => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    /// <summary>指定年月の所定勤務日数（土日・祝日を除く日数）。</summary>
    public static int WorkingDayCount(int year, int month)
    {
        var holidays = GetHolidays(year);
        int days = DateTime.DaysInMonth(year, month);
        int count = 0;
        for (int d = 1; d <= days; d++)
        {
            var date = new DateOnly(year, month, d);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (holidays.Contains(date)) continue;
            count++;
        }
        return count;
    }

    /// <summary>その年の祝日一覧（振替休日・国民の休日を含む）。</summary>
    public static HashSet<DateOnly> GetHolidays(int year)
    {
        if (_cache.TryGetValue(year, out var cached)) return cached;
        var set = Build(year);
        _cache[year] = set;
        return set;
    }

    private static HashSet<DateOnly> Build(int year)
    {
        // 基本の国民の祝日（振替・国民の休日を除く）。
        var baseList = new List<DateOnly>();
        void Add(int m, int d) => baseList.Add(new DateOnly(year, m, d));
        DateOnly NthMonday(int m, int nth)
        {
            var first = new DateOnly(year, m, 1);
            int offset = (((int)DayOfWeek.Monday - (int)first.DayOfWeek) + 7) % 7;
            return first.AddDays(offset + (nth - 1) * 7);
        }

        Add(1, 1);                       // 元日
        baseList.Add(NthMonday(1, 2));   // 成人の日
        Add(2, 11);                      // 建国記念の日
        if (year >= 2020) Add(2, 23);    // 天皇誕生日
        Add(3, VernalEquinox(year));     // 春分の日
        Add(4, 29);                      // 昭和の日
        Add(5, 3);                       // 憲法記念日
        Add(5, 4);                       // みどりの日
        Add(5, 5);                       // こどもの日
        baseList.Add(NthMonday(7, 3));   // 海の日
        if (year >= 2016) Add(8, 11);    // 山の日
        baseList.Add(NthMonday(9, 3));   // 敬老の日
        Add(9, AutumnalEquinox(year));   // 秋分の日
        baseList.Add(NthMonday(10, 2));  // スポーツの日
        Add(11, 3);                      // 文化の日
        Add(11, 23);                     // 勤労感謝の日

        var baseSet = new HashSet<DateOnly>(baseList);
        var result = new HashSet<DateOnly>(baseList);

        // 振替休日：祝日が日曜なら、その後の最も近い「祝日でない日」を休日にする。
        foreach (var h in baseList)
        {
            if (h.DayOfWeek != DayOfWeek.Sunday) continue;
            var n = h.AddDays(1);
            while (result.Contains(n)) n = n.AddDays(1);
            result.Add(n);
        }

        // 国民の休日：前後を祝日に挟まれた平日（日曜・祝日でない日）を休日にする。
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            if (baseSet.Contains(d) || d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (baseSet.Contains(d.AddDays(-1)) && baseSet.Contains(d.AddDays(1)))
                result.Add(d);
        }

        return result;
    }

    // 春分の日（近似式・2000〜2099）。
    private static int VernalEquinox(int year)
        => (int)(20.8431 + 0.242194 * (year - 1980) - (year - 1980) / 4);

    // 秋分の日（近似式・2000〜2099）。
    private static int AutumnalEquinox(int year)
        => (int)(23.2488 + 0.242194 * (year - 1980) - (year - 1980) / 4);
}
