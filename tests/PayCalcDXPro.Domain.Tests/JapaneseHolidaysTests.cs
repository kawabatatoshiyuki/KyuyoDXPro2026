using PayCalcDXPro.Domain.Calendar;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>祝日のオフライン算出の検証（2026年）。</summary>
public class JapaneseHolidaysTests
{
    [Theory]
    [InlineData(1, 1)]    // 元日
    [InlineData(1, 12)]   // 成人の日（第2月曜）
    [InlineData(2, 11)]   // 建国記念の日
    [InlineData(2, 23)]   // 天皇誕生日
    [InlineData(3, 20)]   // 春分の日
    [InlineData(4, 29)]   // 昭和の日
    [InlineData(5, 3)]    // 憲法記念日
    [InlineData(5, 4)]    // みどりの日
    [InlineData(5, 5)]    // こどもの日
    [InlineData(5, 6)]    // 振替休日（5/3が日曜）
    [InlineData(7, 20)]   // 海の日（第3月曜）
    [InlineData(8, 11)]   // 山の日
    [InlineData(9, 21)]   // 敬老の日（第3月曜）
    [InlineData(9, 22)]   // 国民の休日（敬老の日と秋分の日に挟まれる）
    [InlineData(9, 23)]   // 秋分の日
    [InlineData(10, 12)]  // スポーツの日（第2月曜）
    [InlineData(11, 3)]   // 文化の日
    [InlineData(11, 23)]  // 勤労感謝の日
    public void 二千二十六年の祝日を判定できる(int month, int day)
        => Assert.True(JapaneseHolidays.IsHoliday(new DateOnly(2026, month, day)));

    [Theory]
    [InlineData(1, 2)]    // 平日
    [InlineData(5, 7)]    // 連休明けの平日
    [InlineData(12, 25)]  // 祝日でない
    public void 平日は祝日でない(int month, int day)
        => Assert.False(JapaneseHolidays.IsHoliday(new DateOnly(2026, month, day)));

    [Fact]
    public void 所定勤務日数は土日祝を除外する()
    {
        // 2026年5月：GW（5/3日,5/4,5/5,5/6振替）を含む。
        int weekdaysOnly = 0;
        for (int d = 1; d <= 31; d++)
        {
            var w = new DateOnly(2026, 5, d).DayOfWeek;
            if (w is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) weekdaysOnly++;
        }
        int withHolidays = JapaneseHolidays.WorkingDayCount(2026, 5);
        Assert.True(withHolidays < weekdaysOnly); // 祝日の分だけ少ない
        // 5月の平日祝日：5/4(月),5/5(火),5/6(水・振替) の3日が平日かつ祝日。
        Assert.Equal(weekdaysOnly - 3, withHolidays);
    }
}
