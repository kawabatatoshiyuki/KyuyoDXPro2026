namespace PayCalcDXPro.Domain.Models;

// 給与所得の源泉徴収票等の法定調書合計表（現行 R_合計表）の集計入出力モデル。

/// <summary>給与所得の源泉徴収票（従業員1人分）。現行 T_従業員情報×T_年末調整。</summary>
public sealed record 給与支払行
{
    public decimal 給与賞与計 { get; init; }
    public decimal 前社収入 { get; init; }
    /// <summary>乙欄区分が「年調」(現行 乙欄=="1") か。年調なら年調年税額、その他なら源泉計を税額に用いる。</summary>
    public bool 年調 { get; init; }
    public decimal 年調年税額 { get; init; }
    public decimal 源泉計 { get; init; }
    public decimal 前社源泉 { get; init; }
    public bool 源泉徴収票提出 { get; init; }

    /// <summary>税額に用いる値（年調なら年調年税額、その他なら源泉計）。</summary>
    public decimal 採用税額 => 年調 ? 年調年税額 : 源泉計;
}

/// <summary>退職所得の源泉徴収票（1人分）。現行 T_退職源泉情報。</summary>
public sealed record 退職支払行
{
    public bool 提出 { get; init; }
    public decimal 支払金額1 { get; init; }
    public decimal 支払金額2 { get; init; }
    public decimal 支払金額3 { get; init; }
    public decimal 源泉徴収税額1 { get; init; }
    public decimal 源泉徴収税額2 { get; init; }
    public decimal 源泉徴収税額3 { get; init; }

    public decimal 支払金額計 => 支払金額1 + 支払金額2 + 支払金額3;
    public decimal 源泉徴収税額計 => 源泉徴収税額1 + 源泉徴収税額2 + 源泉徴収税額3;
}

/// <summary>支払を受ける者の区分。現行 支払受者区分（法人=1 / 個人=2）。</summary>
public enum 支払受者区分
{
    法人 = 1,
    個人 = 2,
}

/// <summary>報酬、料金、契約金及び賞金の支払調書（1件分）。現行 T_年調報酬情報。</summary>
public sealed record 報酬支払行
{
    public bool 提出 { get; init; }
    public 支払受者区分 受者区分 { get; init; }
    /// <summary>区分1（1号〜8号該当）。</summary>
    public int 号 { get; init; }
    /// <summary>支払金額1〜5（合計して使用）。</summary>
    public IReadOnlyList<decimal> 支払金額 { get; init; } = Array.Empty<decimal>();
    /// <summary>源泉徴収税額1〜5（合計して使用）。</summary>
    public IReadOnlyList<decimal> 源泉徴収税額 { get; init; } = Array.Empty<decimal>();

    public decimal 支払金額計 => 支払金額.Sum();
    public decimal 源泉徴収税額計 => 源泉徴収税額.Sum();
}

/// <summary>不動産系の支払調書（使用料・譲受・あっせん共通の1件分）。現行 T_不動産情報/T_不動産譲受/T_不動産あっせん情報。</summary>
public sealed record 不動産支払行
{
    /// <summary>支払調書の提出が必要か。現行は列名「不要」だが値=="1"が「提出が必要」を意味する。</summary>
    public bool 提出要 { get; init; }
    /// <summary>支払金額（フォームにより1〜5/1〜3/1〜2。合計して使用）。</summary>
    public IReadOnlyList<decimal> 支払金額 { get; init; } = Array.Empty<decimal>();

    public decimal 支払金額計 => 支払金額.Sum();
}

/// <summary>人員・支払金額・源泉徴収税額の集計枠（源泉の無い区分は0）。</summary>
public sealed record 区分集計
{
    public int 人員 { get; init; }
    public decimal 支払金額 { get; init; }
    public decimal 源泉徴収税額 { get; init; }
}

/// <summary>報酬支払調書の号別集計（個人/法人の人員と金額）。</summary>
public sealed record 報酬号別集計
{
    public required int 号 { get; init; }
    public int 個人人員 { get; init; }
    public int 法人人員 { get; init; }
    public decimal 支払金額 { get; init; }
    public decimal 源泉徴収税額 { get; init; }
}

/// <summary>報酬支払調書の合計（A計・提出分の人員/金額）。</summary>
public sealed record 報酬合計集計
{
    public int 個人人員 { get; init; }
    public int 法人人員 { get; init; }
    public decimal 支払金額 { get; init; }
    public decimal 源泉徴収税額 { get; init; }
}

/// <summary>法定調書合計表（6区分）の集計結果。</summary>
public sealed record LegalRecordSummary
{
    // 1. 給与所得の源泉徴収票合計表
    public required 区分集計 給与_総額 { get; init; }
    public int 給与_源泉徴収税額のない者 { get; init; }
    public required 区分集計 給与_提出 { get; init; }

    // 2. 退職所得の源泉徴収票合計表
    public required 区分集計 退職_総額 { get; init; }
    public required 区分集計 退職_提出 { get; init; }

    // 3. 報酬、料金、契約金及び賞金の支払調書合計表
    public required IReadOnlyList<報酬号別集計> 報酬_号別 { get; init; }
    public required 報酬合計集計 報酬_A計 { get; init; }
    public required 報酬合計集計 報酬_提出 { get; init; }

    // 4. 不動産使用料等の支払調書合計表
    public required 区分集計 不動産_総額 { get; init; }
    public required 区分集計 不動産_提出 { get; init; }

    // 5. 不動産等の譲受けの対価の支払調書合計表
    public required 区分集計 譲受_総額 { get; init; }
    public required 区分集計 譲受_提出 { get; init; }

    // 6. 不動産等の売買又は貸付けのあっせん手数料の支払調書合計表
    public required 区分集計 あっせん_総額 { get; init; }
    public required 区分集計 あっせん_提出 { get; init; }
}
