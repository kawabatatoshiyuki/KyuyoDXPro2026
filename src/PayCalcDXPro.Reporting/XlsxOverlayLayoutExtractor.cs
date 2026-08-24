using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using PayCalcDXPro.Reporting.Models;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// xlsxマスターテンプレートから、プレースホルダ「**…」セルの配置（mm座標・サイズ・揃え・フォント）を
/// 自動抽出する（方式①）。列幅・行高は ClosedXML が極細列（方眼様式）で誤読するため、生のシートXMLから
/// 直接読む。背景PDFは同じxlsxを印刷したものなので、ここで得た座標へ値を重ねれば様式に整合する。
/// xlsxを編集（セル移動）すれば座標も自動追従する。
/// </summary>
public static class XlsxOverlayLayoutExtractor
{
    private const double MaxDigitWidth = 7.0; // Calibri 11 の最大数字幅(px)
    private const double MmPerPx = 25.4 / 96.0;
    private const double MmPerPt = 25.4 / 72.0;
    private const double A4幅Mm = 210.0;
    private const double A4高Mm = 297.0;

    public static IReadOnlyDictionary<string, PdfOverlayField> Extract(Stream xlsx, double scaleOverride = 0)
    {
        // マーカーが1つの場合を前提に辞書化（重複は最後が勝つ）。単票様式（法定調書合計表など）向け。
        var dict = new Dictionary<string, PdfOverlayField>();
        foreach ((string marker, PdfOverlayField field) in ExtractAll(xlsx, scaleOverride))
            dict[marker] = field;
        return dict;
    }

    /// <summary>
    /// 同一マーカーが複数セルに現れる様式（例：退職源泉の4面付け＝1マーカーが4か所）向けに、
    /// 重複を保持したまま (マーカー, 配置) を全件返す。描画側は各配置へ同じ値を重ねる。
    /// </summary>
    public static IReadOnlyList<(string marker, PdfOverlayField field)> ExtractAll(Stream xlsx, double scaleOverride = 0)
    {
        byte[] bytes;
        using (var ms = new MemoryStream()) { xlsx.CopyTo(ms); bytes = ms.ToArray(); }

        Geometry geo = ParseGeometry(bytes);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        IXLWorksheet ws = wb.Worksheets.First();

        double marginLeftMm = ws.PageSetup.Margins.Left * 25.4;
        double marginTopMm = ws.PageSetup.Margins.Top * 25.4;
        double marginRightMm = ws.PageSetup.Margins.Right * 25.4;
        double marginBottomMm = ws.PageSetup.Margins.Bottom * 25.4;

        // 印刷領域（無指定なら使用範囲）。様式は印刷領域を用紙の印刷可能域に合わせて印刷される。
        (int 原点行, int 原点列, int 終端行, int 終端列) = 印刷領域(ws, geo);

        // 用紙の向き（横向き様式＝退職源泉のようにA4を横使いする場合は幅/高を入れ替える）。
        bool landscape = ws.PageSetup.PageOrientation == XLPageOrientation.Landscape;
        double 用紙幅Mm = landscape ? A4高Mm : A4幅Mm;
        double 用紙高Mm = landscape ? A4幅Mm : A4高Mm;
        double printW = 用紙幅Mm - marginLeftMm - marginRightMm;
        double printH = 用紙高Mm - marginTopMm - marginBottomMm;
        double rawW = geo.ColPrefixMm(終端列 + 1) - geo.ColPrefixMm(原点列);
        double rawH = geo.RowPrefixMm(終端行 + 1) - geo.RowPrefixMm(原点行);

        // 列方向はExcelの列幅(文字)→px公式が実フォントと一致せず累積過小になるため、
        // 印刷領域幅が印刷可能域を満たすよう較正する（横はフィット）。
        // 行方向は行高(pt)が実寸に一致するため自然値（縦は印刷領域が用紙内に収まる範囲で等倍）。
        double colScale = scaleOverride > 0 ? scaleOverride : (rawW > 0 ? printW / rawW : 1.0);
        double rowScale = rawH > printH ? printH / rawH : 1.0;

        var layout = new List<(string, PdfOverlayField)>();
        foreach (IXLCell cell in ws.CellsUsed(c => c.DataType == XLDataType.Text && c.GetString().StartsWith("**")))
        {
            (int 行, int 列, int 行数, int 列数) = セル範囲(cell);

            double rawX = geo.ColPrefixMm(列) - geo.ColPrefixMm(原点列);
            double rawY = geo.RowPrefixMm(行) - geo.RowPrefixMm(原点行);
            double 幅 = geo.ColPrefixMm(列 + 列数) - geo.ColPrefixMm(列);
            double 高 = geo.RowPrefixMm(行 + 行数) - geo.RowPrefixMm(行);

            layout.Add((cell.GetString(), new PdfOverlayField
            {
                XMm = marginLeftMm + rawX * colScale,
                YMm = marginTopMm + rawY * rowScale,
                幅Mm = 幅 * colScale,
                高Mm = 高 * rowScale,
                フォントPt = cell.Style.Font.FontSize * rowScale,
                揃え = 揃えを判定(cell.Style.Alignment.Horizontal),
            }));
        }
        return layout;
    }

    /// <summary>
    /// 指定したセルアドレス（例 "A2","AL14"）の左上mm座標・幅・高を、ExtractAll と同一の変換で返す。
    /// マーカーに現れない固定ラベル（現行が Drawing.AddShape で囲む甲/乙・有無等）へ、
    /// セル基準＋mmオフセットで丸を重ねるために使う。
    /// </summary>
    public static IReadOnlyDictionary<string, (double xMm, double yMm, double wMm, double hMm)> ExtractCells(
        Stream xlsx, IEnumerable<string> addresses, double scaleOverride = 0)
    {
        byte[] bytes;
        using (var ms = new MemoryStream()) { xlsx.CopyTo(ms); bytes = ms.ToArray(); }

        Geometry geo = ParseGeometry(bytes);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        IXLWorksheet ws = wb.Worksheets.First();

        double marginLeftMm = ws.PageSetup.Margins.Left * 25.4;
        double marginTopMm = ws.PageSetup.Margins.Top * 25.4;
        double marginRightMm = ws.PageSetup.Margins.Right * 25.4;
        double marginBottomMm = ws.PageSetup.Margins.Bottom * 25.4;

        (int 原点行, int 原点列, int 終端行, int 終端列) = 印刷領域(ws, geo);

        bool landscape = ws.PageSetup.PageOrientation == XLPageOrientation.Landscape;
        double 用紙幅Mm = landscape ? A4高Mm : A4幅Mm;
        double 用紙高Mm = landscape ? A4幅Mm : A4高Mm;
        double printW = 用紙幅Mm - marginLeftMm - marginRightMm;
        double printH = 用紙高Mm - marginTopMm - marginBottomMm;
        double rawW = geo.ColPrefixMm(終端列 + 1) - geo.ColPrefixMm(原点列);
        double rawH = geo.RowPrefixMm(終端行 + 1) - geo.RowPrefixMm(原点行);
        double colScale = scaleOverride > 0 ? scaleOverride : (rawW > 0 ? printW / rawW : 1.0);
        double rowScale = rawH > printH ? printH / rawH : 1.0;

        var result = new Dictionary<string, (double, double, double, double)>();
        foreach (string addr in addresses)
        {
            IXLCell cell = ws.Cell(addr);
            (int 行, int 列, int 行数, int 列数) = セル範囲(cell);
            double rawX = geo.ColPrefixMm(列) - geo.ColPrefixMm(原点列);
            double rawY = geo.RowPrefixMm(行) - geo.RowPrefixMm(原点行);
            double 幅 = geo.ColPrefixMm(列 + 列数) - geo.ColPrefixMm(列);
            double 高 = geo.RowPrefixMm(行 + 行数) - geo.RowPrefixMm(行);
            result[addr] = (
                marginLeftMm + rawX * colScale,
                marginTopMm + rawY * rowScale,
                幅 * colScale,
                高 * rowScale);
        }
        return result;
    }

    // ---- 生XMLから列幅(文字)・行高(pt)を読む ----

    private sealed class Geometry
    {
        public required double[] ColMm { get; init; } // 1始まり、各列の幅(mm)。index0未使用
        public required double[] RowMm { get; init; }
        public int MaxCol => ColMm.Length - 1;
        public int MaxRow => RowMm.Length - 1;

        public double ColPrefixMm(int colExclusive)
        {
            double s = 0;
            for (int c = 1; c < colExclusive && c <= MaxCol; c++) s += ColMm[c];
            return s;
        }
        public double RowPrefixMm(int rowExclusive)
        {
            double s = 0;
            for (int r = 1; r < rowExclusive && r <= MaxRow; r++) s += RowMm[r];
            return s;
        }
    }

    private static Geometry ParseGeometry(byte[] xlsxBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsxBytes), ZipArchiveMode.Read);
        ZipArchiveEntry sheet = zip.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .First();

        XDocument doc;
        using (var s = sheet.Open()) doc = XDocument.Load(s);

        XElement root = doc.Root!;
        XElement? fmt = root.Elements().FirstOrDefault(e => e.Name.LocalName == "sheetFormatPr");
        double defColChars = ParseD(fmt?.Attribute("defaultColWidth")?.Value, 8.43);
        double defRowPt = ParseD(fmt?.Attribute("defaultRowHeight")?.Value, 15.0);

        // 列幅（cols/col の min..max に width を割当）
        var colChars = new Dictionary<int, double>();
        int maxCol = 1;
        XElement? cols = root.Elements().FirstOrDefault(e => e.Name.LocalName == "cols");
        if (cols != null)
        {
            foreach (XElement col in cols.Elements().Where(e => e.Name.LocalName == "col"))
            {
                int min = (int)ParseD(col.Attribute("min")?.Value, 1);
                int max = (int)ParseD(col.Attribute("max")?.Value, min);
                double w = ParseD(col.Attribute("width")?.Value, defColChars);
                for (int c = min; c <= max; c++) colChars[c] = w;
                if (max > maxCol) maxCol = max;
            }
        }

        // 行高（row の r に ht を割当）
        var rowPt = new Dictionary<int, double>();
        int maxRow = 1;
        XElement? data = root.Elements().FirstOrDefault(e => e.Name.LocalName == "sheetData");
        if (data != null)
        {
            foreach (XElement row in data.Elements().Where(e => e.Name.LocalName == "row"))
            {
                int r = (int)ParseD(row.Attribute("r")?.Value, 0);
                if (r <= 0) continue;
                string? ht = row.Attribute("ht")?.Value;
                rowPt[r] = ht != null ? ParseD(ht, defRowPt) : defRowPt;
                if (r > maxRow) maxRow = r;
            }
        }

        // 印刷領域が <cols>/<row> の定義範囲より右・下に及ぶことがあるため、余裕を持って既定幅で埋める。
        int colSize = Math.Max(maxCol, 300);
        int rowSize = Math.Max(maxRow, 300);

        var colMm = new double[colSize + 2];
        for (int c = 1; c <= colSize; c++)
            colMm[c] = ColCharsToMm(colChars.TryGetValue(c, out var w) ? w : defColChars);

        var rowMm = new double[rowSize + 2];
        for (int r = 1; r <= rowSize; r++)
            rowMm[r] = (rowPt.TryGetValue(r, out var h) ? h : defRowPt) * MmPerPt;

        return new Geometry { ColMm = colMm, RowMm = rowMm };
    }

    private static double ColCharsToMm(double widthChars)
    {
        // Excel列幅(文字)→ピクセル：px = Truncate(((256*w + Truncate(128/MDW))/256)*MDW)
        double px = Math.Truncate(((256 * widthChars + Math.Truncate(128 / MaxDigitWidth)) / 256) * MaxDigitWidth);
        return px * MmPerPx;
    }

    private static double ParseD(string? s, double fallback)
        => double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static (int 原点行, int 原点列, int 終端行, int 終端列) 印刷領域(IXLWorksheet ws, Geometry geo)
    {
        if (ws.PageSetup.PrintAreas.Any())
        {
            IXLRange a = ws.PageSetup.PrintAreas.First();
            return (a.FirstRow().RowNumber(), a.FirstColumn().ColumnNumber(),
                    a.LastRow().RowNumber(), a.LastColumn().ColumnNumber());
        }
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? geo.MaxCol;
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? geo.MaxRow;
        return (1, 1, lastRow, lastCol);
    }

    private static (int row, int col, int rows, int cols) セル範囲(IXLCell cell)
    {
        if (cell.IsMerged())
        {
            IXLRange r = cell.MergedRange();
            return (r.FirstRow().RowNumber(), r.FirstColumn().ColumnNumber(), r.RowCount(), r.ColumnCount());
        }
        return (cell.Address.RowNumber, cell.Address.ColumnNumber, 1, 1);
    }

    private static PdfTextAlign 揃えを判定(XLAlignmentHorizontalValues h) => h switch
    {
        XLAlignmentHorizontalValues.Right => PdfTextAlign.Right,
        XLAlignmentHorizontalValues.Center or XLAlignmentHorizontalValues.CenterContinuous => PdfTextAlign.Center,
        _ => PdfTextAlign.Left,
    };
}
