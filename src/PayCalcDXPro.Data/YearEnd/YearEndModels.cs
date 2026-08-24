namespace PayCalcDXPro.Data.YearEnd;

/// <summary>年末調整の対象者一覧の1行（従業員選択用）。</summary>
public sealed record YearEndTarget(int Number, string No, string 氏名, string 就退区分名, bool Retired);

/// <summary>配偶者（年末調整）。障害区分: 1=一般,2=特別,3=なし。</summary>
public sealed class YearEndSpouse
{
    public string 氏名 { get; set; } = "";
    public string フリガナ { get; set; } = "";
    public string 生年月日 { get; set; } = "";
    public string 収入 { get; set; } = "";        // 現行 列「配偶者収入」(TEXT)
    public string 収入種類 { get; set; } = "";    // 現行 列「配偶者収入種類」
    public string 障害 { get; set; } = "3";       // 1=一般,2=特別,3=なし
    public bool 同居 { get; set; } = true;
    public bool 非居住者 { get; set; }
}

/// <summary>扶養親族（年末調整）。障害: 1/2/3。留学送金: 0=なし,1=留学,2=38万以上送金。</summary>
public sealed class YearEndDependent
{
    public string 氏名 { get; set; } = "";
    public string フリガナ { get; set; } = "";
    public string 生年月日 { get; set; } = "";
    public string 続柄 { get; set; } = "";
    public int 収入 { get; set; }                 // 現行 列「扶養親族収入N」(INTEGER)
    public string 収入種類 { get; set; } = "";    // 現行 列「扶養親族収入種類N」
    public string 障害 { get; set; } = "3";
    public bool 同居 { get; set; } = true;
    public bool 非居住者 { get; set; }
    public string 留学送金 { get; set; } = "0";
}

/// <summary>
/// 年末調整（T_年末調整）の1従業員分。現行 F_年調_従業員情報 の全タブ項目を保持する。
/// 列の区分：入力（本人情報・配偶者扶養・支給情報の各タブ）／集計（給与賞与トランから Aggregate）／結果（年末調整タブ）。
/// </summary>
public sealed class YearEndEntry
{
    public int Number { get; set; }

    // ---- 共通区分 ----
    /// <summary>年調区分（現行 列「乙欄」）：1=年末調整する,2=未済,3=乙欄。</summary>
    public string 乙欄 { get; set; } = "1";
    public bool 源泉徴収票提出 { get; set; }
    public string 源泉徴収票摘要欄追記文 { get; set; } = "";

    // ---- 本人情報タブ：本人区分 ----
    public string 住民税コード { get; set; } = "1";
    /// <summary>寡婦・ひとり親（現行 列「寡婦」）：1=寡婦,2=ひとり親,3=なし。</summary>
    public string 寡婦 { get; set; } = "3";
    /// <summary>本人の障害（現行 列「障害」）：1=一般,2=特別,3=なし。</summary>
    public string 障害 { get; set; } = "3";
    public bool 未成年 { get; set; }
    public bool 勤労学生 { get; set; }
    public bool 死亡退職 { get; set; }
    public bool 災害者 { get; set; }
    public bool 外国人 { get; set; }
    /// <summary>所得金額調整控除の適用（現行 列「所得金額調整控除適用区分」。共働きの23歳未満/特別障害扶養）。</summary>
    public bool 所得金額調整控除適用 { get; set; }
    public string 所得金額調整控除適用の扶養者氏名 { get; set; } = "";
    /// <summary>所得金額調整控除の実効適用（手動フラグ＋自動判定。DeriveAutoDeductions が算定。非永続）。</summary>
    public bool 所得金額調整控除実効適用 { get; set; }

    // ---- 本人情報タブ：転職情報 ----
    public bool 就職 { get; set; }
    public string 中途就職日 { get; set; } = "";
    public bool 退職 { get; set; }
    public string 中途退職日 { get; set; } = "";
    public string 前社の名称 { get; set; } = "";
    public string 所在地 { get; set; } = "";
    public int 前社収入 { get; set; }
    public int 前社社保 { get; set; }
    public int 前社源泉 { get; set; }
    public string 前社退職日 { get; set; } = "";

    // ---- 本人情報タブ：非課税通勤手当の調整 ----
    public int 非課税通勤手当金額 { get; set; }            // 現行 列「金額」
    public string 非課税通勤手当算出根拠 { get; set; } = ""; // 現行 列「算出根拠」

    // ---- 本人情報タブ：保険料等 ----
    public int 新生命保険 { get; set; }
    public int 生命保険 { get; set; }
    public int 介護医療保険 { get; set; }
    public int 新個人年金 { get; set; }
    public int 個人年金 { get; set; }
    public int 申告社保 { get; set; }
    public int 国民年金保険料等 { get; set; }
    public int 地震保険 { get; set; }
    public int 長期損害 { get; set; }
    public int 小規模共済 { get; set; }

    // ---- 本人情報タブ：住宅借入金等特別控除 ----
    public int 住宅控除 { get; set; }                 // 住宅借入金等特別控除額
    public int 新築購入借入金 { get; set; }
    public string 新築購入居住開始 { get; set; } = "";
    public string 新築購入適用区分 { get; set; } = "";
    public int 増改築借入金 { get; set; }
    public string 増改築居住開始 { get; set; } = "";
    public string 増改築適用区分 { get; set; } = "";
    public int 住宅借入金特別控除可能額 { get; set; }

    // ---- 配偶者・扶養者情報タブ ----
    public YearEndSpouse 配偶者 { get; set; } = new();
    public YearEndDependent[] 扶養親族 { get; } = { new(), new(), new(), new(), new(), new(), new() };

    // ---- 計算用の補助値（年末調整タブの計算が配偶者・扶養親族から算定して設定） ----
    public bool 配偶者あり { get; set; }
    public int 配偶者所得 { get; set; }
    public bool 配偶者70歳以上 { get; set; }
    public int 扶養控除等合計 { get; set; }
    public int 特定親族特別控除額 { get; set; }

    // ---- 支給情報タブ：年間集計（給与・賞与トランから自動） ----
    public int[] 給与 { get; } = new int[12];
    public int[] 賞与 { get; } = new int[4];
    public int[] 社保 { get; } = new int[12];
    public int[] 社保賞与 { get; } = new int[4];
    public int[] 源泉 { get; } = new int[12];
    public int[] 源泉賞与 { get; } = new int[4];
    // 各支給の扶養親族等の数（源泉徴収簿の月別「扶養親族等の数」欄。現行 給与扶養親族数N月／賞与扶養親族数N回目）。
    public int[] 給与扶養親族数 { get; } = new int[12];
    public int[] 賞与扶養親族数 { get; } = new int[4];

    public int 給与計 { get; set; }
    public int 給与社保計 { get; set; }
    public int 給与源泉計 { get; set; }
    public int 賞与計 { get; set; }
    public int 賞与社保計 { get; set; }
    public int 賞与源泉計 { get; set; }
    public int 給与賞与計 { get; set; }
    public int 源泉計 { get; set; }

    // ---- 年末調整タブ：計算結果 ----
    public int 給与所得控除後 { get; set; }
    public int 給与所得控除後調整控除後 { get; set; }
    public int 所得金額調整控除額 { get; set; }
    public int 生保控除 { get; set; }
    public int 地震控除 { get; set; }
    public int 配偶者特別控除 { get; set; }
    public int 基礎控除 { get; set; }
    public int 所得控除計 { get; set; }
    public int 課税所得 { get; set; }
    public int 所得税 { get; set; }
    public int 年調所得税額 { get; set; }
    public int 年調年税額 { get; set; }
    /// <summary>過不足額＝源泉計 − 年調年税額（＋＝過納〔還付〕、−＝不足〔追加徴収〕）。</summary>
    public int 過不足額 { get; set; }
}
