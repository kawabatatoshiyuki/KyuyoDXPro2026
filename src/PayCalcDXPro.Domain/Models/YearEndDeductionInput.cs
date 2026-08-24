namespace PayCalcDXPro.Domain.Models;

/// <summary>
/// 年末調整を所得控除明細から完結させるための入力。
/// 基礎控除・配偶者控除・生命保険料控除は表から計算し、その他の控除は確定値で受け取る。
/// </summary>
public sealed record YearEndDeductionInput
{
    public required decimal 年間給与収入 { get; init; }
    public bool 所得金額調整控除の適用 { get; init; }

    // 社会保険料等
    public decimal 社会保険料控除 { get; init; }          // 給与天引きの社会保険料
    public decimal 申告社会保険料 { get; init; }
    public decimal 小規模企業共済等掛金 { get; init; }

    // 生命保険料（支払額）
    public decimal 介護医療保険料 { get; init; }
    public decimal 一般生命保険料_新 { get; init; }
    public decimal 一般生命保険料_旧 { get; init; }
    public decimal 個人年金保険料_新 { get; init; }
    public decimal 個人年金保険料_旧 { get; init; }

    public decimal 地震保険料控除 { get; init; }

    // 配偶者
    public bool 配偶者あり { get; init; }
    public decimal 配偶者合計所得 { get; init; }
    public bool 配偶者70歳以上 { get; init; }

    /// <summary>扶養控除・障害者控除・寡婦/ひとり親・勤労学生控除の合計（別途算定して投入）。</summary>
    public decimal 扶養控除等合計 { get; init; }

    /// <summary>特定親族特別控除の合計（現行 shinzKojoTotal、別途算定して投入）。</summary>
    public decimal 特定親族特別控除合計 { get; init; }

    public decimal 住宅借入金等特別控除 { get; init; }
}
