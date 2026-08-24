using PayCalcDXPro.Domain.Formatting;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

public class InputFormatTests
{
    // ---- 郵便番号 ----

    [Theory]
    [InlineData("1234567", "1234567")]
    [InlineData("123-4567", "1234567")]
    [InlineData("１２３４５６７", "1234567")]   // 全角
    [InlineData("123 4567", "1234567")]
    [InlineData("12345678", "1234567")]       // 7桁で打ち切り
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 郵便番号は数字のみ最大7桁へ正規化される(string? input, string expected)
        => Assert.Equal(expected, InputFormat.NormalizePostalCode(input));

    [Theory]
    [InlineData("1234567", "123-4567")]
    [InlineData("123-4567", "123-4567")]
    [InlineData("12", "12")]               // 7桁未満は素のまま
    public void 郵便番号は7桁ならハイフン整形される(string input, string expected)
        => Assert.Equal(expected, InputFormat.FormatPostalCode(input));

    [Theory]
    [InlineData("1234567", true)]
    [InlineData("", true)]                 // 空は許可
    [InlineData("123456", false)]          // 6桁はNG
    public void 郵便番号は空または7桁のみ妥当(string input, bool ok)
        => Assert.Equal(ok, InputFormat.IsValidPostalCode(input));

    // ---- 電話番号 ----

    [Theory]
    [InlineData("03-1234-5678", "03-1234-5678")]
    [InlineData("０３－１２３４－５６７８", "03-1234-5678")] // 全角
    [InlineData("03(1234)5678", "0312345678")]            // 記号除去（括弧）
    [InlineData("--03--1234--", "03-1234")]               // 連続/前後ハイフン整理
    public void 電話番号は半角化し数字とハイフンのみへ正規化される(string input, string expected)
        => Assert.Equal(expected, InputFormat.NormalizePhone(input));

    [Theory]
    [InlineData("03-1234-5678", true)]   // 10桁
    [InlineData("090-1234-5678", true)]  // 11桁
    [InlineData("", true)]               // 空は許可
    [InlineData("1234", false)]          // 桁不足
    public void 電話番号は空または10から11桁のみ妥当(string input, bool ok)
        => Assert.Equal(ok, InputFormat.IsValidPhone(input));
}
