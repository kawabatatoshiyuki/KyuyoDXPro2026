using System.Security.Cryptography;
using System.Text;

namespace PayCalcDXPro.Data.Legacy;

/// <summary>
/// 現行（デスクトップ版）の暗号化SQLite（AES-128-CBC、鍵=パスワードを16バイトへ0埋め、
/// 先頭16バイトがIV）を復号するユーティリティ。データ移行・突合の入口。
/// 現行 EncryptDecrypt.cs と同一アルゴリズム。
/// </summary>
public static class LegacyDbDecryptor
{
    private const int KeyLength = 16;

    /// <summary>暗号化DBファイルを平文SQLiteへ復号する。</summary>
    public static void DecryptFile(string srcEncrypted, string dstPlain, string password = "SyuhouSystem")
    {
        byte[] key = GenerateByteKey(password);

        using var ifs = File.OpenRead(srcEncrypted);
        using var ofs = File.Create(dstPlain);

        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        byte[] iv = new byte[KeyLength];
        if (ifs.Read(iv, 0, KeyLength) != KeyLength)
            throw new InvalidDataException("IVの読み取りに失敗しました（ファイルが短すぎます）。");
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var cs = new CryptoStream(ofs, decryptor, CryptoStreamMode.Write);
        ifs.CopyTo(cs);
    }

    private static byte[] GenerateByteKey(string password)
    {
        byte[] pw = Encoding.UTF8.GetBytes(password);
        byte[] key = new byte[KeyLength];
        for (int i = 0; i < KeyLength; i++)
            key[i] = i < pw.Length ? pw[i] : (byte)0;
        return key;
    }
}
