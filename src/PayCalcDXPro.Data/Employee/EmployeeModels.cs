using PayCalcDXPro.Data.Company;

namespace PayCalcDXPro.Data.Employee;

/// <summary>従業員編集フォームの選択肢一式（コードマスタ＋部門/役職/銀行）。</summary>
public sealed class EmployeeFormOptions
{
    public IReadOnlyList<CodeOption> 性別 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 就退区分 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 部門 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 役職 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 銀行 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 所得税源泉 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 障害者 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 寡婦夫 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 勤労学生 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 配偶者区分 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 配偶者障害者 { get; set; } = Array.Empty<CodeOption>();
    public IReadOnlyList<CodeOption> 合計所得見積 { get; set; } = Array.Empty<CodeOption>();
}

/// <summary>従業員一覧の1行（表示用）。退職=就退区分が3(退職)/4(死亡退職)。</summary>
public sealed record EmployeeListItem(
    int Number, string No, string 氏名, string フリガナ,
    string 部門名, string 役職名, string 就退区分名, bool 退職);

/// <summary>
/// 従業員（T_従業員情報）の基本情報。Phase1で扱う列のみを保持する。
/// 社会保険・税区分・配偶者/扶養・住民税・残業単価/給与は後続フェーズで追加し、
/// 本モデルでのUPDATEはこれらの列を変更しない（保全する）。
/// </summary>
public sealed class Employee
{
    /// <summary>主キー。0は新規（保存時に採番）。</summary>
    public int Number { get; set; }
    public string No { get; set; } = "";
    public string 氏名 { get; set; } = "";
    public string フリガナ { get; set; } = "";
    public string 郵便番号 { get; set; } = "";
    public string 住所１ { get; set; } = "";
    public string 住所２ { get; set; } = "";
    /// <summary>M_部門情報。未設定=0。</summary>
    public string 部門番号 { get; set; } = "0";
    /// <summary>M_役職情報。未設定=0。</summary>
    public string 役職番号 { get; set; } = "0";
    /// <summary>M_コード 017。1=男, 2=女。</summary>
    public string 性別コード { get; set; } = "1";
    /// <summary>生年月日（"yyyy/MM/dd" 文字列）。</summary>
    public string 生年月日 { get; set; } = "";
    /// <summary>M_コード 014。1=継続,2=中途就職,3=退職,4=死亡退職。</summary>
    public string 就退区分 { get; set; } = "1";
    /// <summary>就職/退職年月日（"yyyy/MM/dd"）。就退区分が2/3/4のとき必須。</summary>
    public string 就退年月日 { get; set; } = "";
    /// <summary>M_銀行情報。現金支給=1。</summary>
    public string 銀行番号 { get; set; } = "1";
    /// <summary>1=普通, 2=当座。</summary>
    public string 預金種類 { get; set; } = "1";
    public string 口座番号 { get; set; } = "";
    public int 並び順 { get; set; }

    // ===== Phase2：社会保険・税区分 =====
    // 社会保険6項目は 1=加入 / 2=未加入。
    public string 健康保険 { get; set; } = "1";
    public string 介護保険 { get; set; } = "2";
    public string 子育支援 { get; set; } = "1";
    public string 厚生年金 { get; set; } = "1";
    public string 年金基金 { get; set; } = "2";
    public string 雇用保険 { get; set; } = "1";
    /// <summary>M_コード 010。1=甲欄,2=乙欄,3=計算しない。</summary>
    public string 所得税源泉 { get; set; } = "1";
    /// <summary>M_コード 015。1=該当しない,2=一般,3=特別。</summary>
    public string 本人障害者 { get; set; } = "1";
    /// <summary>M_コード 006。1=該当しない,2=寡婦,3=ひとり親。</summary>
    public string 本人寡婦夫 { get; set; } = "1";
    /// <summary>M_コード 008。1=該当しない,2=該当する。</summary>
    public string 本人勤労学生 { get; set; } = "1";

    // ===== Phase3：配偶者・扶養親族・住民税 =====
    /// <summary>M_コード 001。1=いない,2=源泉控除対象配偶者,3=同一生計配偶者,4=控除対象外。</summary>
    public string 配偶者区分 { get; set; } = "1";
    /// <summary>M_コード 019。1=該当しない,2=一般,3=特別,4=同居特別。</summary>
    public string 配偶者障害者 { get; set; } = "1";
    /// <summary>M_コード 013。1=900万円以下,2=900万円超。</summary>
    public string 配偶者合計所得見積 { get; set; } = "1";

    public int 扶養親族親族数 { get; set; }
    public int 扶養親族同居老親 { get; set; }
    public int 扶養親族老人 { get; set; }
    public int 扶養親族特定 { get; set; }
    public int 扶養親族同居特別 { get; set; }
    public int 扶養親族特別障害 { get; set; }
    public int 扶養親族一般障害 { get; set; }
    public int 特定親族数 { get; set; }
    /// <summary>源泉徴収税額表の扶養親族等の数（保存時に自動計算）。</summary>
    public int 源泉税額計算の扶養親族数 { get; set; }

    public int 住民税1_5月 { get; set; }
    public int 住民税6月 { get; set; }
    public int 住民税7_12月 { get; set; }

    // ===== Phase4：給与・割増賃金（単価） =====
    public int 報酬月額 { get; set; }
    public int 日給単価 { get; set; }
    public int 法定内残業単価 { get; set; }
    public int 普通残業単価 { get; set; }
    public int 普通残業単価60h超 { get; set; }
    public int 深夜残業単価 { get; set; }
    public int 深夜残業単価60h超 { get; set; }
    public int 休日残業単価 { get; set; }
    public int 休日残業単価60h超 { get; set; }
    public int 休日深夜残業単価 { get; set; }
    public int 休日深夜残業単価60h超 { get; set; }
    // ※賞与累計は賞与処理が更新するためフォーム対象外（保存時に変更しない）。
}
