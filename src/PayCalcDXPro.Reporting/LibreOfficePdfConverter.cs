using System.Diagnostics;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// LibreOffice headless（soffice）で xlsx 等を PDF へ変換する（帳票方式A の最終段）。
/// 官製様式は xlsx テンプレに寸法を作り込んであるため、変換するだけで様式どおりのPDFになる。
/// オンプレ各サーバに LibreOffice を1つ用意し、その soffice の実行パスを渡す。
/// </summary>
public sealed class LibreOfficePdfConverter
{
    private readonly string _sofficePath;

    /// <param name="sofficePath">soffice 実行ファイル。Windowsでは soffice.com を推奨（CLI同期実行向き）。</param>
    public LibreOfficePdfConverter(string sofficePath) => _sofficePath = sofficePath;

    /// <summary>Windows標準の導入先から soffice.com を探す。見つからなければ null。</summary>
    public static string? FindDefault()
    {
        string[] candidates =
        {
            @"C:\Program Files\LibreOffice\program\soffice.com",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.com",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>xlsx を PDF へ変換し、生成された PDF のパスを返す。</summary>
    public string ConvertToPdf(string inputPath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        // 既存のLibreOfficeプロセスとプロファイル競合しないよう、専用のユーザープロファイルを使う。
        string profile = Path.Combine(Path.GetTempPath(), $"lo_profile_{Guid.NewGuid():N}");
        var psi = new ProcessStartInfo
        {
            FileName = _sofficePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add("--norestore");
        psi.ArgumentList.Add($"-env:UserInstallation=file:///{profile.Replace('\\', '/')}");
        psi.ArgumentList.Add("--convert-to");
        psi.ArgumentList.Add("pdf");
        psi.ArgumentList.Add("--outdir");
        psi.ArgumentList.Add(outputDir);
        psi.ArgumentList.Add(inputPath);

        using (var p = Process.Start(psi)!)
        {
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"LibreOffice変換に失敗 (exit {p.ExitCode}): {stderr}{stdout}");
        }

        try { if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true); } catch { /* プロファイル削除失敗は無視 */ }

        string pdf = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
        if (!File.Exists(pdf))
            throw new FileNotFoundException("変換後のPDFが見つかりません。", pdf);
        return pdf;
    }
}
