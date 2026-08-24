using PayCalcDXPro.Data.Employee;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>源泉徴収税額表の扶養親族等の数の算出（現行ロジック移植）の検証。DB不要の純粋ロジック。</summary>
public class EmployeeCalcTests
{
    [Fact]
    public void 既定はゼロ()
        => Assert.Equal(0, EmployeeCalc.源泉扶養親族数(new Employee()));

    [Fact]
    public void 本人区分で加算される()
    {
        Assert.Equal(1, EmployeeCalc.源泉扶養親族数(new Employee { 本人障害者 = "2" }));   // 一般障害
        Assert.Equal(1, EmployeeCalc.源泉扶養親族数(new Employee { 本人寡婦夫 = "3" }));   // ひとり親
        Assert.Equal(1, EmployeeCalc.源泉扶養親族数(new Employee { 本人勤労学生 = "2" })); // 勤労学生
    }

    [Fact]
    public void 配偶者で加算される()
    {
        // 900万円以下 × 源泉控除対象配偶者 → +1
        Assert.Equal(1, EmployeeCalc.源泉扶養親族数(new Employee { 配偶者合計所得見積 = "1", 配偶者区分 = "2" }));
        // 900万円以下 × 同一生計（#179 で同一生計も基礎＋1の対象に）→ +1
        Assert.Equal(1, EmployeeCalc.源泉扶養親族数(new Employee { 配偶者合計所得見積 = "1", 配偶者区分 = "3" }));
        // 900万円以下 × 同一生計 × 同居特別 → +1（基礎）+2（同居特別）= +3
        Assert.Equal(3, EmployeeCalc.源泉扶養親族数(new Employee { 配偶者合計所得見積 = "1", 配偶者区分 = "3", 配偶者障害者 = "4" }));
        // 900万円以下 × 源泉控除対象 × 一般/特別（#179 で区分2も障害者加算の対象に）→ +1（基礎）+1（障害）= +2
        Assert.Equal(2, EmployeeCalc.源泉扶養親族数(new Employee { 配偶者合計所得見積 = "1", 配偶者区分 = "2", 配偶者障害者 = "2" }));
        // 900万円超 × 同一生計 × 一般/特別 → 基礎なし +1（障害）= +1
        Assert.Equal(1, EmployeeCalc.源泉扶養親族数(new Employee { 配偶者合計所得見積 = "2", 配偶者区分 = "3", 配偶者障害者 = "2" }));
        // 900万円超 × 同一生計 × 同居特別 → 基礎なし +2（同居特別）= +2
        Assert.Equal(2, EmployeeCalc.源泉扶養親族数(new Employee { 配偶者合計所得見積 = "2", 配偶者区分 = "3", 配偶者障害者 = "4" }));
    }

    [Fact]
    public void 扶養親族の人数が加算される()
    {
        var e = new Employee
        {
            扶養親族親族数 = 2, 扶養親族一般障害 = 1, 扶養親族特別障害 = 1, 扶養親族同居特別 = 1, 特定親族数 = 1,
            扶養親族同居老親 = 5, 扶養親族老人 = 5, 扶養親族特定 = 5, // これらは源泉数に含めない
        };
        Assert.Equal(6, EmployeeCalc.源泉扶養親族数(e));
    }

    [Fact]
    public void 複合ケース()
    {
        var e = new Employee
        {
            本人障害者 = "2",                                  // +1
            配偶者合計所得見積 = "1", 配偶者区分 = "2",          // +1
            扶養親族親族数 = 3,                                  // +3
        };
        Assert.Equal(5, EmployeeCalc.源泉扶養親族数(e));
    }
}
