using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PayCalcDXPro.Domain.Models;
using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 法定調書合計表（官製様式）を、現行の埋め込みxlsxテンプレート（R_合計表.xlsx）へ
/// 集計結果を流し込んで生成する（帳票方式A）。テンプレートのセルにはVBReport由来の
/// プレースホルダ文字列「**…」が入っており、その文字列を集計値で置換する。
/// 出力はxlsxバイト列。PDF化はサーバ側の LibreOffice headless 変換に委ねる（配置層の責務）。
/// 値が0の人員・金額は現行同様に空欄にする。
/// </summary>
public sealed class LegalRecordSummaryXlsxGenerator
{
    private static readonly Regex 号該当 = new(@"[\(（]([１-８])号該当[\)）]", RegexOptions.Compiled);
    private static readonly Regex 個人番号桁 = new(@"^\*\*提出者_個人番号又は法人番号_(\d{1,2})番号$", RegexOptions.Compiled);

    /// <summary>テンプレートxlsxへ流し込み、生成したxlsxのバイト列を返す。</summary>
    public byte[] Generate(Stream template, LegalRecordSummary summary, 法定調書提出者情報 提出者)
    {
        using var wb = new XLWorkbook(template);
        string 番号13 = Build個人番号13(提出者);

        foreach (IXLWorksheet ws in wb.Worksheets)
        {
            var markers = ws.CellsUsed(c => c.DataType == XLDataType.Text && c.GetString().StartsWith("**")).ToList();
            foreach (IXLCell cell in markers)
            {
                if (TryResolve(cell.GetString(), summary, 提出者, 番号13, out object? value) && value is not null)
                    SetValue(cell, value);
                else
                    cell.Clear(XLClearOptions.Contents); // 未対応／0値は空欄
            }
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 背景PDF生成用に、テンプレートから全プレースホルダ「**…」を消去したxlsxを返す。
    /// これを（開発機の LibreOffice/Excel で）PDF化したものが官製様式の背景になる。
    /// </summary>
    public byte[] ClearMarkers(Stream template)
    {
        using var wb = new XLWorkbook(template);
        foreach (IXLWorksheet ws in wb.Worksheets)
        {
            var markers = ws.CellsUsed(c => c.DataType == XLDataType.Text && c.GetString().StartsWith("**")).ToList();
            foreach (IXLCell cell in markers) cell.Clear(XLClearOptions.Contents);
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>マーカー文字列を集計値へ解決する。matched=false は様式に無い未対応マーカー。value=null は空欄。</summary>
    public static bool TryResolve(string marker, LegalRecordSummary s, 法定調書提出者情報 h, string 番号13, out object? value)
    {
        value = null;

        // --- 提出者（ヘッダ） ---
        switch (marker)
        {
            case "**年": value = Str(h.年); return true;
            case "**署番号": value = Str(h.署番号); return true;
            case "**税務署長": value = Str(h.税務署名); return true;
            case "**事業種目": value = Str(h.事業種目); return true;
            case "**整理番号": value = Str(h.整理番号); return true;
            case "**提出者_住所(居所)又は所在地_電話番号": value = Str(h.電話番号); return true;
            case "**提出者_住所(居所)又は所在地": value = Str(h.所在地); return true;
            case "**提出者_フリガナ_氏名又は名称": value = Str(h.会社名カナ); return true;
            case "**提出者_氏名又は名称": value = Str(h.会社名); return true;
            case "**提出者_フリガナ_代表者氏名": value = Str(h.代表者カナ); return true;
            case "**提出者_代表者氏名": value = Str(h.代表者名); return true;
        }

        Match num = 個人番号桁.Match(marker);
        if (num.Success)
        {
            int idx = int.Parse(num.Groups[1].Value);
            if (idx >= 1 && idx <= 番号13.Length) { value = 番号13[idx - 1].ToString(); return true; }
            return false;
        }

        // --- 集計（6区分） ---
        if (marker.StartsWith("**1.")) return Resolve給与(marker, s, out value);
        if (marker.StartsWith("**2.")) return Resolve退職(marker, s, out value);
        if (marker.StartsWith("**3.")) return Resolve報酬(marker, s, out value);
        if (marker.StartsWith("**4.")) return Resolve不動産(marker, s.不動産_総額, s.不動産_提出, out value);
        if (marker.StartsWith("**5.")) return Resolve不動産(marker, s.譲受_総額, s.譲受_提出, out value);
        if (marker.StartsWith("**6.")) return Resolveあっせん(marker, s, out value);

        return false;
    }

    // ---- 区分別ルーティング ----

    private static bool Resolve給与(string m, LegalRecordSummary s, out object? value)
    {
        var g = m.Contains("源泉徴収票を提出するもの") ? s.給与_提出 : s.給与_総額;
        value =
            m.EndsWith("_源泉徴収税額のない者") ? Cnt(s.給与_源泉徴収税額のない者) :
            m.EndsWith("_人員") ? Cnt(g.人員) :
            m.EndsWith("_支払金額") ? Num(g.支払金額) :
            m.EndsWith("_源泉徴収税額") ? Num(g.源泉徴収税額) :
            (object?)Unknown;
        return value is not Sentinel;
    }

    private static bool Resolve退職(string m, LegalRecordSummary s, out object? value)
    {
        var g = m.Contains("源泉徴収票を提出するもの") ? s.退職_提出 : s.退職_総額;
        value =
            m.EndsWith("_人員") ? Cnt(g.人員) :
            m.EndsWith("_支払金額") ? Num(g.支払金額) :
            m.EndsWith("_源泉徴収税額") ? Num(g.源泉徴収税額) :
            (object?)Unknown;
        return value is not Sentinel;
    }

    private static bool Resolve報酬(string m, LegalRecordSummary s, out object? value)
    {
        if (m.Contains("Ａのうち")) { value = 報酬枠(m, s.報酬_提出); return value is not Sentinel; }
        if (m.Contains("Ａ計")) { value = 報酬枠(m, s.報酬_A計); return value is not Sentinel; }

        Match mt = 号該当.Match(m);
        if (!mt.Success) { value = null; return false; }
        int 号 = "１２３４５６７８".IndexOf(mt.Groups[1].Value[0]) + 1;
        報酬号別集計 row = s.報酬_号別.First(x => x.号 == 号);
        value =
            m.EndsWith("_人員_個人以外") ? Cnt(row.法人人員) :
            m.EndsWith("_人員_個人") ? Cnt(row.個人人員) :
            m.EndsWith("_支払金額") ? Num(row.支払金額) :
            m.EndsWith("_源泉徴収税額") ? Num(row.源泉徴収税額) :
            (object?)Unknown;
        return value is not Sentinel;
    }

    private static object? 報酬枠(string m, 報酬合計集計 g) =>
        m.EndsWith("_人員_個人以外") ? Cnt(g.法人人員) :
        m.EndsWith("_人員_個人") ? Cnt(g.個人人員) :
        m.EndsWith("_支払金額") ? Num(g.支払金額) :
        m.EndsWith("_源泉徴収税額") ? Num(g.源泉徴収税額) :
        Unknown;

    private static bool Resolve不動産(string m, 区分集計 総額, 区分集計 提出, out object? value)
    {
        var g = m.Contains("Ａのうち") ? 提出 : 総額;
        value =
            m.EndsWith("_人員") ? Cnt(g.人員) :
            m.EndsWith("_支払金額") ? Num(g.支払金額) :
            (object?)Unknown;
        return value is not Sentinel;
    }

    private static bool Resolveあっせん(string m, LegalRecordSummary s, out object? value)
    {
        var g = m.Contains("Ａのうち") ? s.あっせん_提出 : s.あっせん_総額;
        value =
            m.EndsWith("_支払金額") ? Num(g.支払金額) :
            m.EndsWith("_人員") ? Cnt(g.人員) :
            (object?)Unknown;
        return value is not Sentinel;
    }

    // ---- 値変換 ----

    /// <summary>未対応マーカーを表す番兵（matched=false に変換）。</summary>
    private sealed class Sentinel { }
    private static readonly Sentinel Unknown = new();

    private static object? Num(decimal v) => v == 0m ? null : (long)v;   // 0は空欄
    private static object? Cnt(int v) => v == 0 ? null : v;               // 0は空欄
    private static string? Str(string? v) => string.IsNullOrEmpty(v) ? null : v;

    private static void SetValue(IXLCell cell, object value)
    {
        switch (value)
        {
            case string str: cell.Value = str; break;
            case long l: cell.Value = l; break;
            case int i: cell.Value = i; break;
            default: cell.Value = value.ToString(); break;
        }
    }

    /// <summary>提出者番号を様式の13桁へ整形（現行 inputNumber 準拠）。</summary>
    public static string Build個人番号13(法定調書提出者情報 h)
    {
        string number = h.事業所番号 ?? "";
        string kubun = h.番号区分 ?? "";
        bool valid = long.TryParse(number, out _) &&
                     ((kubun == "1" && number.Length == 13) || (kubun == "2" && number.Length == 12));
        if (valid)
        {
            if (number.Length == 12) number = " " + number; // 12桁は先頭にスペースを付与して13桁化
        }
        else
        {
            number = new string(' ', 13);
        }
        return number;
    }
}
