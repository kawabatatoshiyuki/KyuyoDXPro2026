namespace PayCalcDXPro.Domain.Models;

/// <summary>扶養親族の年齢区分（12/31時点）。年少=16歳未満（扶養控除の対象外、障害者控除は対象）。</summary>
public enum 扶養年齢区分
{
    年少,        // 16歳未満
    一般,        // 16〜18歳・23〜69歳
    特定扶養,    // 19〜22歳
    老人,        // 70歳以上
}

/// <summary>障害区分。</summary>
public enum 障害区分
{
    なし,
    一般障害,
    特別障害,
}

/// <summary>本人のひとり親・寡婦区分。</summary>
public enum 寡婦ひとり親区分
{
    なし,
    寡婦,
    ひとり親,
}

/// <summary>控除対象扶養親族1人分の属性（収入要件は判定済みの前提）。</summary>
public sealed record 扶養親族
{
    public required 扶養年齢区分 年齢区分 { get; init; }
    public bool 同居 { get; init; }
    public 障害区分 障害 { get; init; } = 障害区分.なし;
}

/// <summary>
/// 扶養・障害者・寡婦・ひとり親・勤労学生の各控除額（現行 T_扶養障害者等 の8区分）。
/// 加算方式：一般扶養を基礎に、特定/老人/障害の各加算を上乗せする。
/// </summary>
public sealed record DependentDeductionTable
{
    public required decimal 一般扶養 { get; init; }      // 380,000
    public required decimal 特定扶養加算 { get; init; }   // 250,000（特定扶養=一般+加算=630,000）
    public required decimal 老人その他加算 { get; init; } // 100,000（老人その他=480,000）
    public required decimal 同居老親加算 { get; init; }   // 200,000（同居老親=580,000）
    public required decimal 一般障害 { get; init; }      // 270,000（寡婦・勤労学生控除も同額）
    public required decimal 特別障害 { get; init; }      // 400,000（非同居）
    public required decimal 同居特別障害 { get; init; }   // 750,000
    public required decimal ひとり親 { get; init; }      // 350,000
}
