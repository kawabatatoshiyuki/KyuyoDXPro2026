using System.Security.Cryptography;
using PayCalcDXPro.Security;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>セキュリティ層：マイナンバー列暗号化の検証。</summary>
public class MyNumberProtectorTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void 暗号化して復号すると元に戻る()
    {
        var p = new MyNumberProtector(Key);
        const string mynumber = "123456789012";

        string enc = p.Encrypt(mynumber);
        string dec = p.Decrypt(enc);

        Assert.NotEqual(mynumber, enc);   // 平文がそのまま出ない
        Assert.Equal(mynumber, dec);
    }

    [Fact]
    public void 同じ平文でも暗号文は毎回変わる_nonceランダム化()
    {
        var p = new MyNumberProtector(Key);
        Assert.NotEqual(p.Encrypt("123456789012"), p.Encrypt("123456789012"));
    }

    [Fact]
    public void 改ざんされた暗号文は復号で例外になる_認証付き暗号()
    {
        var p = new MyNumberProtector(Key);
        char[] enc = p.Encrypt("123456789012").ToCharArray();
        enc[^2] = enc[^2] == 'A' ? 'B' : 'A'; // タグ付近を改変
        Assert.ThrowsAny<CryptographicException>(() => p.Decrypt(new string(enc)));
    }

    [Fact]
    public void マスキングは下4桁のみ残す()
    {
        Assert.Equal("********9012", MyNumberProtector.Mask("123456789012"));
    }
}
