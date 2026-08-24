namespace PayCalcDXPro.Data.MyNumber;

/// <summary>
/// マイナンバー（個人番号）の形式・チェックデジット検証。現行 MFunction.myNumberCheck と一致。
/// 空は許容（true）。12桁・全数字・「000000000000」以外・チェックデジット一致で有効。
/// </summary>
public static class MyNumberValidator
{
    public static bool IsValid(string? input)
    {
        if (string.IsNullOrEmpty(input)) return true;          // 未入力は許容
        if (input.Length != 12 || !input.All(char.IsDigit)) return false;
        if (input == "000000000000") return false;

        // d[0]=最下位(チェックデジット) … d[11]=最上位。
        var d = new int[12];
        for (int i = 0; i < 12; i++) d[i] = input[11 - i] - '0';

        // 上位から 6,5,4,3,2,7,6,5,4,3,2 を掛ける（現行と同じ重み付け）。
        int sum = 0;
        for (int i = 1; i < 12; i++) sum += d[i] * (i < 7 ? i + 1 : i - 5);

        int check = sum % 11;
        check = check <= 1 ? 0 : 11 - check;
        return check == d[0];
    }
}
