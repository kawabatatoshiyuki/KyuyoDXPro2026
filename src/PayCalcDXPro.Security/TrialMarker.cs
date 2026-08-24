using System.Security.Cryptography;
using System.Text;

namespace PayCalcDXPro.Security;

/// <summary>
/// 体験版の「形跡マーカー」(trial.dat) の読み書き・暗号化・署名・検証。
/// 現行デスクトップ版 <c>PayCalcDX.標準モジュール.TrialMarker</c> の移植。
/// 保存先は %ProgramData%\SyuhouSystem\PayCalcDXTrial\trial.dat（全年度共有の永続ファイル）。
/// 暗号(AES-128-CBC/PKCS7・鍵=ENCRYPT_DECRYPT_KEY を16バイト0埋め・先頭16バイトIV)と
/// 署名(SHA-256(year|date|secret)) を現行と完全一致させ、デスクトップ版が書いたファイルと相互運用できる。
/// </summary>
public static class TrialMarker
{
    public enum MarkerStatus
    {
        NotFound,   // ファイル無し（初回起動）
        Valid,      // 署名検証OK
        Tampered,   // 署名検証NG（改ざん/復号失敗/破損）
    }

    public sealed class MarkerInfo
    {
        public MarkerStatus Status;
        public int FirstTrialYear;          // 最初にセットアップした体験版の年度
        public string FirstSetupDate = "";  // 初回セットアップ日時（記録用）
    }

    // 現行 MConst と同値（相互運用のため変更しないこと）。
    private const string TrialFolder = @"SyuhouSystem\PayCalcDXTrial"; // TRIAL_MARKER_FOLDER
    private const string TrialFileName = "trial.dat";                  // TRIAL_MARKER_NAME
    private const string Secret = "PayCalcDXTrial-Marker-2026";        // TRIAL_MARKER_SECRET
    private const string EncryptKey = "SyuhouSystem";                  // ENCRYPT_DECRYPT_KEY
    private const int KeyLength = 16;

    private const string KeyYear = "FirstTrialYear";
    private const string KeyDate = "FirstSetupDate";
    private const string KeySign = "Signature";

    /// <summary>マーカー格納のベースフォルダ。null＝%ProgramData%（テスト時のみ差し替える）。</summary>
    public static string? BaseDirectoryOverride { get; set; }

    /// <summary>trial.dat のフルパス（%ProgramData%\SyuhouSystem\PayCalcDXTrial\trial.dat）。</summary>
    public static string FilePath => Path.Combine(
        BaseDirectoryOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        TrialFolder, TrialFileName);

    /// <summary>マーカーを読み取り、署名を検証する。</summary>
    public static MarkerInfo Read()
    {
        var info = new MarkerInfo();
        if (!File.Exists(FilePath)) { info.Status = MarkerStatus.NotFound; return info; }

        try
        {
            string plain = DecryptFromFile(FilePath);

            string? year = null, date = null, sign = null;
            foreach (string raw in plain.Split('\n'))
            {
                string l = raw.Trim('\r', '\n', ' ');
                if (l.Length == 0) continue;
                int p = l.IndexOf('=');
                if (p < 0) continue;
                string k = l[..p], v = l[(p + 1)..];
                if (k == KeyYear) year = v;
                else if (k == KeyDate) date = v;
                else if (k == KeySign) sign = v;
            }

            // 必須項目欠落・署名不一致・年度数値変換不可 → 改ざん扱い（安全側でブロック）。
            if (year is null || date is null || sign is null
                || !sign.Equals(MakeSignature(year, date), StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(year, out info.FirstTrialYear))
            {
                info.Status = MarkerStatus.Tampered;
                return info;
            }

            info.FirstSetupDate = date;
            info.Status = MarkerStatus.Valid;
            return info;
        }
        catch
        {
            // 復号失敗等は改ざん/破損扱い（安全側でブロック）。
            info.Status = MarkerStatus.Tampered;
            return info;
        }
    }

    /// <summary>現在年度でマーカーを新規作成する（初回セットアップ時）。</summary>
    public static void Write(int firstTrialYear)
    {
        string year = firstTrialYear.ToString();
        string date = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        string sign = MakeSignature(year, date);

        var sb = new StringBuilder();
        sb.Append(KeyYear).Append('=').Append(year).Append('\n');
        sb.Append(KeyDate).Append('=').Append(date).Append('\n');
        sb.Append(KeySign).Append('=').Append(sign).Append('\n');

        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        EncryptToFile(sb.ToString(), FilePath);
    }

    /// <summary>署名生成： SHA-256( year + "|" + date + "|" + 秘密鍵 ) の16進。</summary>
    private static string MakeSignature(string year, string date)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(year + "|" + date + "|" + Secret));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>パスワードを16バイト鍵へ（不足分は0埋め）。現行 EncryptDecrypt.GenerateByteKey と一致。</summary>
    private static byte[] KeyBytes()
    {
        byte[] pw = Encoding.UTF8.GetBytes(EncryptKey);
        var key = new byte[KeyLength];
        for (int i = 0; i < KeyLength; i++) key[i] = i < pw.Length ? pw[i] : (byte)0;
        return key;
    }

    private static void EncryptToFile(string plain, string path)
    {
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = KeyBytes();
        aes.GenerateIV();

        using var ofs = new FileStream(path, FileMode.Create, FileAccess.Write);
        ofs.Write(aes.IV, 0, KeyLength);   // 先頭16バイトにIVを書く
        using var enc = aes.CreateEncryptor();
        using var cs = new CryptoStream(ofs, enc, CryptoStreamMode.Write);
        byte[] data = Encoding.UTF8.GetBytes(plain);
        cs.Write(data, 0, data.Length);
    }

    private static string DecryptFromFile(string path)
    {
        using var ifs = new FileStream(path, FileMode.Open, FileAccess.Read);
        var iv = new byte[KeyLength];
        int got = ifs.Read(iv, 0, KeyLength);
        if (got < KeyLength) throw new InvalidDataException("IVが不足しています。");

        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = KeyBytes();
        aes.IV = iv;

        using var dec = aes.CreateDecryptor();
        using var oms = new MemoryStream();
        using (var cs = new CryptoStream(ifs, dec, CryptoStreamMode.Read))
        {
            cs.CopyTo(oms);
        }
        return Encoding.UTF8.GetString(oms.ToArray());
    }
}
