using PayCalcDXPro.Reporting;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>西暦→和暦変換（現行 adToJapCal 相当）の検証。</summary>
public class WarekiTests
{
    [Theory]
    [InlineData(2025, "7")]   // 令和7
    [InlineData(2026, "8")]   // 令和8
    [InlineData(2019, "1")]   // 令和元年（2019/05〜）
    public void 和暦年数を返す(int 西暦, string 期待)
    {
        Assert.Equal(期待, Wareki.年数(西暦));
    }

    [Theory]
    [InlineData(2025, "令和")]
    [InlineData(2018, "平成")]
    public void 元号名を返す(int 西暦, string 期待)
    {
        Assert.Equal(期待, Wareki.元号(西暦));
    }
}
