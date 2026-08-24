namespace PayCalcDXPro.Data.Bonus;

/// <summary>
/// 1人・1回分の賞与トラン（現行 T_賞与トラン の1行）。
/// 主キー＝Number（従業員）＋回（同一年度内の賞与支給回 1〜4）。
/// 賞与は月次給与と異なり「非課税・通勤費・住民税」が無く、課税対象＝総支給−社会保険合計。
/// 支給項目は16（手当項目1〜12＋任意項目1〜4）、控除項目は9。金額は円（整数）。
/// </summary>
public sealed class BonusEntry
{
    public string Number { get; set; } = "";
    /// <summary>賞与支給回（1〜4。現行 T_賞与トラン.回）。</summary>
    public int 回 { get; set; }

    // ---- 支給項目 ----
    /// <summary>賞与（基本）。現行 txt_賞与。</summary>
    public int? 賞与 { get; set; }
    /// <summary>賞与支給項目1〜16（index0＝賞与支給1）。現行 手当項目1〜12＋任意項目1〜4。</summary>
    public int?[] 賞与支給 { get; set; } = new int?[16];
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
    /// <summary>控除項目1〜9（index0＝控除1）。賞与は総控除に控除9まで含める（給与と異なる）。</summary>
    public int?[] 控除項目 { get; set; } = new int?[9];
    public int? 総控除額 { get; set; }
    public int? 差引支給額 { get; set; }

    public int? データ個数 { get; set; }

    /// <summary>源泉徴収上の扶養親族等の数（現行 T_賞与トラン.扶養親族数＝保存時の fuyoShinzokuSu）。</summary>
    public int? 扶養親族数 { get; set; }

    // ---- 年末調整・その他 ----
    public int? 還付金額 { get; set; }
    public int? 徴収金額 { get; set; }
    public string? 備考 { get; set; }
}

/// <summary>
/// 賞与計算ブリッジ（<see cref="BonusProcessor"/>）が必要とする従業員側の設定。
/// 現行 T_従業員情報 のうち賞与計算に効く項目だけを抜き出したもの。保険加入フラグは「加入なら true」。
/// </summary>
public sealed class BonusEmployeeSettings
{
    // ---- 社会保険の加入フラグ（true=加入） ----
    public bool 健康保険加入 { get; set; }
    public bool 介護保険加入 { get; set; }
    public bool 子育支援加入 { get; set; }
    public bool 厚生年金加入 { get; set; }
    public bool 年金基金加入 { get; set; }
    public bool 雇用保険加入 { get; set; }

    /// <summary>労働保険適用事業所区分（"1"=一般／"2"=一般以外）。雇用保険料率の選択に使う。</summary>
    public string 労働保険適用 { get; set; } = "1";

    public Payroll.源泉区分 源泉区分 { get; set; } = Payroll.源泉区分.甲;

    /// <summary>源泉税額計算上の扶養親族等の数（賞与算出率表 甲 の列選択に使う。7以上は7人以上列）。</summary>
    public int 源泉扶養親族数 { get; set; }
}

/// <summary>
/// 賞与計算に必要な「当回より前の情報」。DB（T_賞与トラン／T_給与トラン）から組み立てて渡す。
/// </summary>
public sealed class BonusContext
{
    /// <summary>
    /// 同一年度内で当回より前に支給済みの各回の賞与総支給額（健保・介護の標準賞与額 年度上限通算に使う）。
    /// 現行は 当回が2回目以降のとき1〜(当回-1)回の総支給を加算する。
    /// </summary>
    public IReadOnlyList<int> 既支給賞与総支給 { get; set; } = System.Array.Empty<int>();

    /// <summary>
    /// 前月給与の課税対象額（賞与所得税の算出率を引くキー＝前月の社会保険料控除後の給与額）。
    /// 前月給与トランが無い場合は null（＝賞与所得税は0）。
    /// </summary>
    public int? 前月給与課税対象額 { get; set; }
}
