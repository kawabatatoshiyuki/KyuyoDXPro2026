namespace PayCalcDXPro.Data.Payroll;

/// <summary>
/// 1人・1支給回分の給与トラン（現行 T_給与トラン の1行）。
/// 主キー＝Number（従業員）＋支給回（支払日マスタの支給月1〜12に対応）。
/// 入力項目（基本給・手当・勤怠・残業時間・通勤費・控除）と計算結果（各保険・所得税・各合計）を保持する。
/// 金額は円（整数）、勤怠日数・残業時間は小数。
/// </summary>
public sealed class PayrollEntry
{
    public string Number { get; set; } = "";
    public int 支給回 { get; set; }

    // ---- 支給項目 ----
    public int? 基本給 { get; set; }
    /// <summary>給与支給手当項目1〜12（index0=項目1）。実支給額。非課税かどうかは支給控除項目マスタ側。</summary>
    public int?[] 給与支給手当 { get; set; } = new int?[12];
    public int? 時間外手当 { get; set; }
    public int? 勤怠控除 { get; set; }
    public int? 課税通勤費 { get; set; }
    public int? 非課税通勤費 { get; set; }
    public int? 総支給額 { get; set; }

    // ---- 控除（社会保険・税）----
    public int? 健康保険 { get; set; }
    public int? 介護保険 { get; set; }
    public int? 子育支援 { get; set; }
    public int? 小規模共済 { get; set; }
    public int? 厚生年金 { get; set; }
    public int? 年金基金 { get; set; }
    public int? 雇用保険 { get; set; }
    public int? 社会保険合計 { get; set; }
    public int? 課税対象額 { get; set; }
    public int? 所得税 { get; set; }
    public int? 住民税 { get; set; }
    /// <summary>控除項目1〜9（index0=項目1）。任意の追加控除額。</summary>
    public int?[] 控除項目 { get; set; } = new int?[9];
    public int? 総控除額 { get; set; }
    public int? 差引支給額 { get; set; }

    public int? データ個数 { get; set; }
    public int? 扶養親族数 { get; set; }

    // ---- 勤怠（日数・時間。小数可）----
    public double? 所定出勤日数 { get; set; }
    public double? 出勤日数 { get; set; }
    public double? 労働時間 { get; set; }
    public double? 欠勤日数_一般 { get; set; }
    public double? 欠勤日数_病気等 { get; set; }
    public double? 特別休暇 { get; set; }
    public double? 代休 { get; set; }
    public double? 有給休暇 { get; set; }
    public double? 有給残日数 { get; set; }
    public double? 遅刻早退 { get; set; }

    // ---- 残業時間（時間。小数可）----
    public double? 法定内残業 { get; set; }
    public double? 普通残業60h以下 { get; set; }
    public double? 深夜残業60h以下 { get; set; }
    public double? 休日出勤60h以下 { get; set; }
    public double? 休日深夜60h以下 { get; set; }
    public double? 普通残業60h超 { get; set; }
    public double? 深夜残業60h超 { get; set; }
    public double? 休日出勤60h超 { get; set; }
    public double? 休日深夜60h超 { get; set; }

    // ---- 年末調整・その他 ----
    public int? 還付金額 { get; set; }
    public int? 徴収金額 { get; set; }
    public string? 備考 { get; set; }
    public int? 非課税金額計 { get; set; }
}

/// <summary>源泉徴収区分（従業員マスタ 所得税源泉 のコード値）。</summary>
public enum 源泉区分 { 甲 = 1, 乙 = 2, 計算しない = 3 }

/// <summary>
/// 計算ブリッジ（<see cref="PayrollProcessor"/>）が必要とする従業員側の設定。
/// 現行 T_従業員情報 のうち給与計算に効く項目だけを抜き出したもの。
/// 保険加入フラグは「加入なら true」（現行は "1"=加入）。
/// </summary>
public sealed class PayrollEmployeeSettings
{
    // ---- 社会保険の加入フラグ（true=加入） ----
    public bool 健康保険加入 { get; set; }
    public bool 介護保険加入 { get; set; }
    public bool 子育支援加入 { get; set; }
    public bool 厚生年金加入 { get; set; }
    public bool 年金基金加入 { get; set; }
    public bool 雇用保険加入 { get; set; }

    /// <summary>標準報酬月額を引くための報酬月額（0以下なら保険料はすべて0）。</summary>
    public int 報酬月額 { get; set; }

    /// <summary>労働保険適用事業所区分（"1"=一般／"2"=一般以外）。雇用保険料率の選択に使う。</summary>
    public string 労働保険適用 { get; set; } = "1";

    public 源泉区分 源泉区分 { get; set; } = 源泉区分.甲;

    /// <summary>源泉税額計算上の扶養親族等の数（現行 源泉税額計算の扶養親族数）。</summary>
    public int 源泉扶養親族数 { get; set; }

    /// <summary>残業手当・日給の端数処理（1=四捨五入／2=切捨／3=切上、現行 残業手当計算端数処理）。</summary>
    public int 端数処理 { get; set; } = 2;

    public int 日給単価 { get; set; }

    // ---- 残業単価（9種、現行 T_従業員情報）----
    public int 法定内残業単価 { get; set; }
    public int 普通残業単価 { get; set; }
    public int 普通残業単価60h超 { get; set; }
    public int 深夜残業単価 { get; set; }
    public int 深夜残業単価60h超 { get; set; }
    public int 休日残業単価 { get; set; }
    public int 休日残業単価60h超 { get; set; }
    public int 休日深夜残業単価 { get; set; }
    public int 休日深夜残業単価60h超 { get; set; }

    /// <summary>給与支給手当項目1〜12が非課税か（支給控除項目マスタ 給与支給非課税区分="1"）。index0=項目1。</summary>
    public bool[] 非課税手当 { get; set; } = new bool[12];
}
