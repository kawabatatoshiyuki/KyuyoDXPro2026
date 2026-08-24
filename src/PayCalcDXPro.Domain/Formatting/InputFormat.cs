using System.Text;

namespace PayCalcDXPro.Domain.Formatting;

/// <summary>
/// 画面入力の正規化・表示整形・検証を集約する純粋関数群。
/// 基本方針は「データは生値（記号なし）・表示は整形済み」。金額は表示でカンマ、保存は数値そのもの。
/// </summary>
public static class InputFormat
{
    /// <summary>全角数字・各種ハイフン類を半角へ寄せる。null安全。</summary>
    public static string ToHalfWidth(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= '０' && c <= '９') sb.Append((char)(c - '０' + '0'));
            else if (c is 'ー' or '－' or '―' or '‐' or '−' or '‒' or '–') sb.Append('-');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---- 郵便番号 ----

    /// <summary>郵便番号を数字のみ・最大7桁へ正規化する（保存用の生値）。</summary>
    public static string NormalizePostalCode(string? s)
    {
        string half = ToHalfWidth(s);
        var sb = new StringBuilder(7);
        foreach (char c in half)
        {
            if (char.IsDigit(c)) sb.Append(c);
            if (sb.Length == 7) break;
        }
        return sb.ToString();
    }

    /// <summary>7桁なら「123-4567」、それ以外は正規化済みの数字をそのまま返す（表示用）。</summary>
    public static string FormatPostalCode(string? s)
    {
        string d = NormalizePostalCode(s);
        return d.Length == 7 ? d[..3] + "-" + d[3..] : d;
    }

    /// <summary>空、または正確に7桁ならOK。</summary>
    public static bool IsValidPostalCode(string? s)
    {
        int len = NormalizePostalCode(s).Length;
        return len is 0 or 7;
    }

    // ---- 電話番号 ----

    /// <summary>半角化し、数字とハイフン以外を除去。連続・前後のハイフンを整理する。</summary>
    public static string NormalizePhone(string? s)
    {
        string half = ToHalfWidth(s);
        var sb = new StringBuilder(half.Length);
        foreach (char c in half)
            if (char.IsDigit(c) || c == '-') sb.Append(c);

        string r = sb.ToString();
        while (r.Contains("--")) r = r.Replace("--", "-");
        return r.Trim('-');
    }

    /// <summary>空、またはハイフンを除いた数字が10〜11桁ならOK（日本の固定/携帯）。</summary>
    public static bool IsValidPhone(string? s)
    {
        int digits = 0;
        foreach (char c in NormalizePhone(s))
            if (char.IsDigit(c)) digits++;
        return digits is 0 or 10 or 11;
    }
}
