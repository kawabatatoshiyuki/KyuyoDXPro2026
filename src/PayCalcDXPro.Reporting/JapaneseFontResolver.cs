using PdfSharp.Fonts;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// PdfSharp 用の日本語フォントリゾルバ。Windows標準の日本語フォント（OS同梱＝顧客導入物ではない）を読み込む。
/// 完全自己完結を要する場合は、TTFをアセットとして同梱しパスを差し替える。
/// </summary>
public sealed class JapaneseFontResolver : IFontResolver
{
    public const string FamilyName = "PayCalcJp";

    // Windows同梱のゴシック系を優先順に探す（TTC可）。
    private static readonly string[] Candidates =
    {
        @"C:\Windows\Fonts\meiryo.ttc",
        @"C:\Windows\Fonts\YuGothR.ttc",
        @"C:\Windows\Fonts\msgothic.ttc",
        @"C:\Windows\Fonts\BIZ-UDGothicR.ttc",
    };

    private readonly byte[] _font;

    public JapaneseFontResolver()
    {
        string? path = Candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("日本語フォントが見つかりません（Windows標準フォント未検出）。");
        // .ttc（フォントコレクション）は先頭フォントを単体sfntへ変換してから渡す。
        _font = TrueTypeCollection.ToSingleFont(File.ReadAllBytes(path));
    }

    public byte[]? GetFont(string faceName) => _font;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new FontResolverInfo(FamilyName);

    /// <summary>プロセス内で一度だけ登録する。</summary>
    public static void EnsureRegistered()
    {
        if (GlobalFontSettings.FontResolver is JapaneseFontResolver) return;
        GlobalFontSettings.FontResolver = new JapaneseFontResolver();
    }
}
