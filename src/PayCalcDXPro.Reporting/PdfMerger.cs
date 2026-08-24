using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 複数のPDFバイト列を1つの複数ページPDFへ結合する（各PDFのページを順に取り込む）。
/// 法定調書の印刷ハブ（現行 F_法定調書印刷）で、選択した各帳票を1つのプレビューにまとめる用途。
/// </summary>
public static class PdfMerger
{
    /// <summary>空でないPDFを順に結合する。0件なら空配列、1件ならそのまま返す。</summary>
    public static byte[] Merge(IReadOnlyList<byte[]> pdfs)
    {
        var parts = pdfs.Where(p => p is { Length: > 0 }).ToList();
        if (parts.Count == 0) return Array.Empty<byte>();
        if (parts.Count == 1) return parts[0];

        using var outDoc = new PdfDocument();
        foreach (byte[] bytes in parts)
        {
            using var ms = new MemoryStream(bytes);
            using PdfDocument doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            foreach (PdfPage page in doc.Pages) outDoc.AddPage(page);
        }
        using var outMs = new MemoryStream();
        outDoc.Save(outMs);
        return outMs.ToArray();
    }
}
