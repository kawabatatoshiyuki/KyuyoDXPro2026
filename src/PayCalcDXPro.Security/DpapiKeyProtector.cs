using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace PayCalcDXPro.Security;

/// <summary>
/// SQLCipher 鍵やマイナンバー列暗号鍵を Windows DPAPI で保護してファイル保存する。
/// 鍵をコード/設定に平文で置かず、OS の保護領域（マシン or ユーザー）に紐付ける。
/// 既定は LocalMachine（サービスアカウントで常駐する想定）。
/// </summary>
[SupportedOSPlatform("windows")]
public static class DpapiKeyProtector
{
    /// <summary>鍵（バイト列）を DPAPI で保護してファイルへ保存する。</summary>
    public static void ProtectToFile(byte[] key, string path,
        DataProtectionScope scope = DataProtectionScope.LocalMachine)
    {
        byte[] encrypted = ProtectedData.Protect(key, optionalEntropy: null, scope);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, encrypted);
    }

    /// <summary>DPAPI で保護されたファイルから鍵を復元する。</summary>
    public static byte[] UnprotectFromFile(string path,
        DataProtectionScope scope = DataProtectionScope.LocalMachine)
    {
        byte[] encrypted = File.ReadAllBytes(path);
        return ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope);
    }

    /// <summary>鍵ファイルが無ければ新規生成して保護保存し、その鍵を返す（初回セットアップ用）。</summary>
    public static byte[] LoadOrCreate(string path, int keySizeBytes = 32,
        DataProtectionScope scope = DataProtectionScope.LocalMachine)
    {
        if (File.Exists(path)) return UnprotectFromFile(path, scope);

        byte[] key = RandomNumberGenerator.GetBytes(keySizeBytes);
        ProtectToFile(key, path, scope);
        return key;
    }
}
