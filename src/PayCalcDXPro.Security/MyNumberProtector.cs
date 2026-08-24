using System.Security.Cryptography;
using System.Text;

namespace PayCalcDXPro.Security;

/// <summary>
/// マイナンバー等の機微情報を「列単位」で暗号化/復号する（AES-256-GCM = 認証付き暗号）。
///
/// 現行は DB ファイル全体を AES-128-CBC（パスワード→単純パディング鍵）で暗号化していたが、
/// サーバ常駐の Web 化に伴い、(1) DB全体は SQLCipher で透過暗号化、(2) マイナンバー列は本クラスで
/// 二重に列暗号化、という多層防御に置き換える。鍵は本番では Windows DPAPI / キーストアで保護し、
/// 本クラスへは復号済みの 256bit 鍵を注入する。
/// </summary>
public sealed class MyNumberProtector
{
    private const int NonceSize = 12; // 96bit (GCM 推奨)
    private const int TagSize = 16;   // 128bit
    private readonly byte[] _key;     // 32 bytes (256bit)

    public MyNumberProtector(byte[] key256)
    {
        if (key256 is null || key256.Length != 32)
            throw new ArgumentException("鍵は256bit(32バイト)である必要があります。", nameof(key256));
        _key = key256;
    }

    /// <summary>平文 → Base64。格納形式は nonce(12) || 暗号文 || 認証タグ(16)。</summary>
    public string Encrypt(string plaintext)
    {
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        byte[] result = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, result, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + cipher.Length, TagSize);
        return Convert.ToBase64String(result);
    }

    /// <summary>Base64 → 平文。改ざんがあれば例外（認証付き暗号の利点）。</summary>
    public string Decrypt(string encoded)
    {
        byte[] all = Convert.FromBase64String(encoded);
        ReadOnlySpan<byte> nonce = all.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> tag = all.AsSpan(all.Length - TagSize, TagSize);
        ReadOnlySpan<byte> cipher = all.AsSpan(NonceSize, all.Length - NonceSize - TagSize);
        byte[] plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>画面表示用マスキング（例: 123456789012 → ********9012）。復号権限がある場合のみ全桁表示。</summary>
    public static string Mask(string myNumber)
    {
        if (string.IsNullOrEmpty(myNumber)) return myNumber;
        if (myNumber.Length <= 4) return new string('*', myNumber.Length);
        return new string('*', myNumber.Length - 4) + myNumber[^4..];
    }
}
