using System.Security.Cryptography;

namespace PayCalcDXPro.Security;

/// <summary>
/// パスワードのハッシュ化と検証（PBKDF2-HMAC-SHA256）。
/// 現行はアカウントのパスワードを平文相当で保持していたため、移行時にハッシュ化して強化する。
/// 保存形式: "pbkdf2$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;"。
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000; // OWASP 推奨水準

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out int iterations)) return false;

        byte[] salt = Convert.FromBase64String(parts[2]);
        byte[] expected = Convert.FromBase64String(parts[3]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // タイミング攻撃に強い比較
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
