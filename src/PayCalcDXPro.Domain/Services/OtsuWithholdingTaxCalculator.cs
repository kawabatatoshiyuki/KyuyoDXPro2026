using PayCalcDXPro.Domain.Models;

namespace PayCalcDXPro.Domain.Services;

/// <summary>
/// 源泉所得税「乙欄」（従たる給与）の計算。現行 F_給与処理 の所得税源泉="2" 分岐に一致。
/// 課税対象額（課税支給 − 社会保険料）の区分により5通りで算出し、扶養親族1人につき1,610円控除する。
/// </summary>
public sealed class OtsuWithholdingTaxCalculator
{
    // 現行 MConst 同値
    private const decimal 区分1上限 = 104_999m;
    private const decimal 税率_区分1 = 3.063m;       // OTSUZEI_GAKU3
    private const decimal 区分2上限 = 740_000m;
    private const decimal 税率A = 2.5m;              // OTSUZEI_ZEIRITSUA
    private const decimal 税率B = 1.5m;              // OTSUZEI_ZEIRITSUB
    private const decimal 復興率C = 1.021m;          // OTSUZEI_ZEIRITSUC
    private const decimal 区分3上限 = 1_709_999m;
    private const decimal 固定_未満 = 259_200m;      // OTSUZEI_1710K_MIMAN_KOTEIGAKU
    private const decimal 差引_未満 = 740_000m;      // OTSUZEI_1710K_MIMAN_SASHIHIKIGAKU
    private const decimal 割合_未満 = 40.84m;        // OTSUZEI_1710K_MIMAN_WARIAI
    private const decimal 固定_以上 = 655_400m;      // OTSUZEI_1710K_IJO_KOTEIGAKU
    private const decimal 差引_以上 = 1_710_000m;    // OTSUZEI_1710K_IJO_SASHIHIKIGAKU
    private const decimal 割合_以上 = 45.945m;       // OTSUZEI_1710K_IJO_WARIAI
    private const decimal 扶養1人控除 = 1_610m;

    private readonly OtsuTaxTables _t;

    public OtsuWithholdingTaxCalculator(OtsuTaxTables tables) => _t = tables;

    public decimal Calculate(decimal 課税対象額, int 扶養親族等の数)
    {
        decimal otsu;

        if (課税対象額 <= 0)
        {
            otsu = 0m;
        }
        else if (課税対象額 <= 区分1上限)
        {
            otsu = Math.Floor(課税対象額 * 税率_区分1 / 100m);
        }
        else if (課税対象額 <= 区分2上限)
        {
            otsu = 区分2を計算(課税対象額);
        }
        else if (課税対象額 <= 区分3上限)
        {
            otsu = Math.Floor(固定_未満 + (課税対象額 - 差引_未満) * 割合_未満 / 100m);
        }
        else
        {
            otsu = Math.Floor(固定_以上 + (課税対象額 - 差引_以上) * 割合_以上 / 100m);
        }

        // 扶養親族控除（1人 1,610円）
        if (扶養親族等の数 > 0)
        {
            otsu -= 扶養1人控除 * 扶養親族等の数;
            if (otsu < 0) otsu = 0m;
        }

        return otsu;
    }

    // 課税対象額 105,000〜740,000：階差で計算基準額を求め、税率2.5%と1.5%の差から補間する（現行準拠）
    private decimal 区分2を計算(decimal 課税対象額)
    {
        decimal 基準額;
        if (課税対象額 == 区分2上限)
        {
            基準額 = 課税対象額;
        }
        else
        {
            乙税階差Row 階差行 = _t.乙税階差.First(r => 課税対象額 >= r.給与額下限 && 課税対象額 < r.給与額上限);
            decimal 余り = (課税対象額 - 階差行.給与額下限) % 階差行.階差;
            基準額 = 課税対象額 - 余り;
        }

        decimal otsu2 = _t.乙税第2表控除額;

        decimal zA = 基準額 * 税率A;
        zA = 第3表(zA - 第1表(zA) - otsu2);

        decimal zB = 基準額 * 税率B;
        zB = 第3表(zB - 第1表(zB) - otsu2);

        decimal zC = zA - zB;
        zC = Math.Round(zC / 100m, MidpointRounding.AwayFromZero) * 100m;

        return Math.Round(zC * 復興率C / 100m, MidpointRounding.AwayFromZero) * 100m;
    }

    /// <summary>乙税第1表（給与所得控除）：Ceiling(額×率 + 加算額)。</summary>
    private decimal 第1表(decimal 額)
    {
        BracketRow row = _t.乙税第1表.First(r => 額 >= r.下限 && 額 < r.上限);
        return Math.Ceiling(額 * row.率 + row.加算額);
    }

    /// <summary>乙税第3表（税率速算）：額×税率 − 控除額。</summary>
    private decimal 第3表(decimal 額)
    {
        TaxRateRow row = _t.乙税第3表.First(r => 額 >= r.下限 && 額 < r.上限);
        return 額 * row.税率 - row.控除額;
    }
}
