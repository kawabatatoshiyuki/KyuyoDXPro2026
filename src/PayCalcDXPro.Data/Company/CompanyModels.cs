namespace PayCalcDXPro.Data.Company;

/// <summary>会社情報（M_会社情報 の1レコード）。画面とのバインド用。</summary>
public sealed class CompanyInfo
{
    public string 会社名カナ { get; set; } = "";
    public string 会社名 { get; set; } = "";
    public string 事業所名 { get; set; } = "";
    /// <summary>郵便番号（数字のみ7桁の生値。表示は「123-4567」）。</summary>
    public string 郵便番号 { get; set; } = "";
    public string 所在地カナ { get; set; } = "";
    public string 所在地 { get; set; } = "";
    public string 代表者氏名カナ { get; set; } = "";
    public string 代表者名 { get; set; } = "";
    public string ＴＥＬ { get; set; } = "";
    public string 連絡者名カナ { get; set; } = "";
    public string 連絡者名 { get; set; } = "";
    public string 事業種目 { get; set; } = "";

    /// <summary>1=法人, 2=個人事業主。</summary>
    public string 番号区分 { get; set; } = "1";

    /// <summary>法人の場合は法人番号(13桁)。個人の場合のマイナンバーはセキュア入力で別途扱う（次段）。</summary>
    public string 事業所マイナンバー { get; set; } = "";

    public string 税務署名 { get; set; } = "";
    public string 署番号 { get; set; } = "";
    public string 整理番号 { get; set; } = "";
    public string 支払方法 { get; set; } = "";
    public string 支払金融機関 { get; set; } = "";
    public string 金融機関所在地 { get; set; } = "";

    /// <summary>M_コード 020。</summary>
    public string 労働保険適用事業所 { get; set; } = "1";
    /// <summary>M_コード 021。</summary>
    public string 年末調整方法 { get; set; } = "1";
    /// <summary>M_コード 022。</summary>
    public string 残業手当計算端数処理 { get; set; } = "1";

    /// <summary>支給日登録フラグ（0/1）。会社情報単体では既存値を保持する。</summary>
    public string 支給日登録フラグ { get; set; } = "0";
}

/// <summary>コンボ用のコード選択肢（M_コード）。</summary>
public sealed record CodeOption(string コード, string 値);

/// <summary>部門（M_部門情報）の編集行。番号は保存時に採番するため画面では保持しない。</summary>
public sealed class DepartmentRow
{
    public string 部門名 { get; set; } = "";
}

/// <summary>役職（M_役職情報）の編集行。</summary>
public sealed class PositionRow
{
    public string 役職名 { get; set; } = "";
}

/// <summary>銀行（M_銀行情報）の編集行。</summary>
public sealed class BankRow
{
    public string 銀行名 { get; set; } = "";
    public string 支店名 { get; set; } = "";
}

/// <summary>報告書提出先（M_報告書提出先情報）の編集行。</summary>
public sealed class RecipientRow
{
    public string 市区町村名 { get; set; } = "";
    public string 徴収義務者番号 { get; set; } = "";
}

/// <summary>
/// 会社情報に付随する4つのマスタ（部門/役職/銀行/報告書提出先）の編集セット。
/// いずれも先頭に固定レコード（未設定/現金支給/特別徴収対象外）を持ち、ユーザー行はそれを除いた分。
/// </summary>
public sealed class CompanyMasters
{
    public List<DepartmentRow> 部門 { get; set; } = new();
    public List<PositionRow> 役職 { get; set; } = new();
    public List<BankRow> 銀行 { get; set; } = new();
    public List<RecipientRow> 提出先 { get; set; } = new();
}
