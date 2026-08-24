using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 生年月日から扶養親族の年齢区分を判定する。現行 ProcessAgeDateSet / MFunction.chkNenrei に一致。
/// 判定基準は「生年月日 ≦ (判定年−年齢)/01/01 ならその年齢『以上』」「生年月日 ≧ (判定年−年齢)/01/02 なら『未満』」。
/// 判定年は現行 SYR_NEN（年調処理年）に相当。
/// </summary>
public static class AgeClassifier
{
    /// <summary>扶養親族の年齢区分（老人70+／特定扶養19-22／一般16-18・23-69／年少16未満）。</summary>
    public static 扶養年齢区分 扶養区分(DateOnly 生年月日, int 判定年)
    {
        if (生年月日 <= 以上境界(判定年, 70)) return 扶養年齢区分.老人;
        if (生年月日 <= 以上境界(判定年, 19) && 生年月日 >= 未満境界(判定年, 23)) return 扶養年齢区分.特定扶養;
        if (生年月日 <= 以上境界(判定年, 16)) return 扶養年齢区分.一般;
        return 扶養年齢区分.年少;
    }

    /// <summary>70歳以上か（老人控除対象配偶者の判定に使用）。</summary>
    public static bool は70歳以上(DateOnly 生年月日, int 判定年) => 生年月日 <= 以上境界(判定年, 70);

    /// <summary>65歳以上か（公的年金等控除の年齢区分）。現行 chkNenrei(65_IJYOU) に一致。</summary>
    public static bool は65歳以上(DateOnly 生年月日, int 判定年) => 生年月日 <= 以上境界(判定年, 65);

    /// <summary>16歳以上か（控除対象扶養親族の基礎判定）。</summary>
    public static bool は16歳以上(DateOnly 生年月日, int 判定年) => 生年月日 <= 以上境界(判定年, 16);

    /// <summary>19歳以上23歳未満か（特定親族特別控除の年齢要件）。現行 chkNenrei(19_IJYOU, 23_MIMAN) に一致。</summary>
    public static bool は19歳以上23歳未満(DateOnly 生年月日, int 判定年)
        => 生年月日 <= 以上境界(判定年, 19) && 生年月日 >= 未満境界(判定年, 23);

    /// <summary>23歳未満か（所得金額調整控除の扶養要件）。現行 chkNenrei("", 23_MIMAN) に一致。</summary>
    public static bool は23歳未満(DateOnly 生年月日, int 判定年) => 生年月日 >= 未満境界(判定年, 23);

    // 「以上」の上限境界＝(判定年−年齢)/01/01、「未満」の下限境界＝(判定年−年齢)/01/02
    private static DateOnly 以上境界(int 判定年, int 年齢) => new(判定年 - 年齢, 1, 1);
    private static DateOnly 未満境界(int 判定年, int 年齢) => new(判定年 - 年齢, 1, 2);
}
