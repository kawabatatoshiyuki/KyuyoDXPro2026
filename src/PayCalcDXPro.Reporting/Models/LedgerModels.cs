namespace PayCalcDXPro.Reporting.Models;

/// <summary>従業員台帳（現行 R_従業員台帳 準拠の1人1ページ・フォーム）。値は表示用に解決済みの文字列。</summary>
public sealed record EmpLedgerModel
{
    public string 氏名 { get; init; } = "";
    public string 氏名かな { get; init; } = "";
    public string 性別 { get; init; } = "";
    public string 生年月日 { get; init; } = "";
    public string 従業員No { get; init; } = "";
    public string 郵便番号 { get; init; } = "";
    public string 住所 { get; init; } = "";
    public string 勤務の状況 { get; init; } = "";
    public string 中途就職退職年月日 { get; init; } = "";
    public string 事由 { get; init; } = "";
    public string 部門 { get; init; } = "";
    public string 職名 { get; init; } = "";

    // 所得税（本人／配偶者）
    public string 源泉所得税 { get; init; } = "";
    public string 本人障害者 { get; init; } = "";
    public string 寡婦ひとり親 { get; init; } = "";
    public string 勤労学生 { get; init; } = "";
    public string 配偶者区分 { get; init; } = "";
    public string 配偶者障害者 { get; init; } = "";
    public string 配偶者合計所得 { get; init; } = "";

    // 扶養者情報
    public string 扶養親族数 { get; init; } = "";
    public string 内同居親等 { get; init; } = "";
    public string 内老人扶養 { get; init; } = "";
    public string 内特定扶養 { get; init; } = "";
    public string 内特別障害 { get; init; } = "";
    public string 内一般障害 { get; init; } = "";
    public string 特定親族 { get; init; } = "";

    // 住民税
    public string 住民税前期 { get; init; } = "";
    public string 住民税6月 { get; init; } = "";
    public string 住民税後期 { get; init; } = "";

    // 社会保険
    public string 報酬月額 { get; init; } = "";
    public string 健康保険 { get; init; } = "";
    public string 介護保険 { get; init; } = "";
    public string 子育支援 { get; init; } = "";
    public string 厚生年金 { get; init; } = "";
    public string 年金基金 { get; init; } = "";
    public string 雇用保険 { get; init; } = "";

    // 残業単価（法定内＋法定外：上段60h以下／下段60h超）
    public string 法定内 { get; init; } = "";
    public string 普通 { get; init; } = "";
    public string 深夜 { get; init; } = "";
    public string 休日出勤 { get; init; } = "";
    public string 休日深夜 { get; init; } = "";
    public string 普通超 { get; init; } = "";
    public string 深夜超 { get; init; } = "";
    public string 休日出勤超 { get; init; } = "";
    public string 休日深夜超 { get; init; } = "";

    // 支給方法
    public string 現金支給 { get; init; } = "";
    public string 口座振込 { get; init; } = "";
    public string 支給口座情報 { get; init; } = "";
    public string 日給単価 { get; init; } = "";

    public string 備考 { get; init; } = "";
}

/// <summary>導入設定台帳（現行 R_導入設定台帳 準拠）。会社情報＋各マスタ＋支給日＋支給控除項目。</summary>
public sealed record SetupLedgerModel
{
    public string 日付 { get; init; } = "";
    // 会社情報
    public string 区分 { get; init; } = "";
    public string 会社名カナ { get; init; } = "";
    public string 会社名 { get; init; } = "";
    public string 所在地カナ { get; init; } = "";
    public string 所在地 { get; init; } = "";
    public string 代表者氏名カナ { get; init; } = "";
    public string 代表者名 { get; init; } = "";
    public string 連絡者名カナ { get; init; } = "";
    public string 連絡者名 { get; init; } = "";
    public string 事業所名 { get; init; } = "";
    public string 電話番号 { get; init; } = "";
    public string 事業種目 { get; init; } = "";
    public string マイナンバー { get; init; } = "";
    public string 税務署名 { get; init; } = "";
    public string 署番号 { get; init; } = "";
    public string 整理番号 { get; init; } = "";
    public string 支払方法 { get; init; } = "";
    public string 支払金融機関 { get; init; } = "";
    public string 金融機関所在地 { get; init; } = "";
    public string 労働保険 { get; init; } = "";
    public string 年末調整方法 { get; init; } = "";
    public string 残業端数 { get; init; } = "";

    public IReadOnlyList<string> 部門 { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> 役職 { get; init; } = Array.Empty<string>();
    public IReadOnlyList<(string 銀行名, string 支店名)> 銀行 { get; init; } = Array.Empty<(string, string)>();
    public IReadOnlyList<(string 市区町村, string 番号)> 提出先 { get; init; } = Array.Empty<(string, string)>();

    public string 締月 { get; init; } = "";
    public string 締日付 { get; init; } = "";
    /// <summary>基準の支給日（例「25日」）。締め日の横に表示。</summary>
    public string 基準支給日 { get; init; } = "";
    /// <summary>月別（支給回, 支給(月/日), 対象年月, 所定日数）。</summary>
    public IReadOnlyList<(string 回, string 支給, string 対象, string 所定)> 月別 { get; init; } = Array.Empty<(string, string, string, string)>();
    public IReadOnlyList<(string 回, string 支給)> 賞与 { get; init; } = Array.Empty<(string, string)>();

    /// <summary>給与支給手当（名称, 非課税）。空名称はテンプレ同様に枠だけ。</summary>
    public IReadOnlyList<(string 名称, bool 非課税)> 給与手当 { get; init; } = Array.Empty<(string, bool)>();
    public IReadOnlyList<string> 給与控除 { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> 賞与手当 { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> 賞与控除 { get; init; } = Array.Empty<string>();
}
