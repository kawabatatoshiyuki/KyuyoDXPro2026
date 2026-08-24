namespace PayCalcDXPro.Data.PayDay;

/// <summary>支払日情報（M_支給日情報）。1社1レコード。</summary>
public sealed class PayDayInfo
{
    public int 支給年 { get; set; } = DateTime.Now.Year;
    /// <summary>基準の支給日（日）。</summary>
    public int? 支給日 { get; set; }
    /// <summary>締月（前月/当月などの区分。現行値を保持）。</summary>
    public string 締月 { get; set; } = "";
    /// <summary>締日付（"末" または 1〜31）。</summary>
    public string 締日付 { get; set; } = "";

    /// <summary>月別の支給予定（最大12件）。</summary>
    public List<MonthlyPayDay> 月別 { get; set; } = new();
    /// <summary>賞与の支給予定（最大4件）。</summary>
    public List<BonusPayDay> 賞与 { get; set; } = new();
}

/// <summary>月別の支給予定1件。処理済みフラグは給与処理が更新するため画面では変更しない。</summary>
public sealed class MonthlyPayDay
{
    public int? 支給月 { get; set; }
    public int? 支給日 { get; set; }
    public int? 対象年 { get; set; }
    public int? 対象月 { get; set; }
    public int? 所定勤務日数 { get; set; }
    /// <summary>処理済みフラグ（保全）。</summary>
    public string 処理済みフラグ { get; set; } = "";
}

/// <summary>賞与の支給予定1件。処理済みフラグは賞与処理が更新するため画面では変更しない。</summary>
public sealed class BonusPayDay
{
    public int? 支給月 { get; set; }
    public int? 支給日 { get; set; }
    /// <summary>処理済みフラグ（保全）。</summary>
    public string 処理済みフラグ { get; set; } = "";
}
