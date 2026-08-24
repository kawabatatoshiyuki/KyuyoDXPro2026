using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 手順2 Approach B：官製様式を QuestPDF で「mm精度」描画できるかの検証。
/// 列幅・行高を mm 固定でグリッド化し、罫線つきフォームを正確な寸法で出力する。
/// Excel/LibreOffice 等の外部依存なしでサーバ側生成できるのが利点。
/// </summary>
public sealed class FormLayoutPdfGenerator
{
    static FormLayoutPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>mm固定のセル格子でフォーム枠を描いたPDFを返す。</summary>
    public byte[] Generate()
    {
        // 源泉徴収票上部の「支払を受ける者」ブロックを模した mm 指定のセル定義（ラベル, 幅mm, 高mm）。
        var cells = new (string label, double wMm, double hMm)[]
        {
            ("種別", 25, 8),
            ("支払金額", 40, 8),
            ("給与所得控除後の金額", 45, 8),
            ("所得控除の額の合計額", 45, 8),
            ("源泉徴収税額", 30, 8),
        };

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);            // 210 x 297 mm
                page.Margin(10, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic").FontSize(8));

                page.Content().Column(col =>
                {
                    col.Item().Text("令和6年分 給与所得の源泉徴収票（Approach B / mm精度描画 PoC）")
                        .FontSize(11).Bold();
                    col.Item().PaddingTop(4).Text("各セルは mm 固定（幅・高）。罫線つきフォームを正確な寸法で再現できることを示す。")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        foreach (var c in cells)
                        {
                            row.ConstantItem((float)c.wMm, Unit.Millimetre)
                               .Height((float)c.hMm, Unit.Millimetre)
                               .Border(0.5f)
                               .Padding(2)
                               .Text(c.label).FontSize(7);
                        }
                    });

                    // 値行（高さ12mm固定）
                    col.Item().Row(row =>
                    {
                        var sampleValues = new[] { "給料・賞与", "4,200,000", "3,000,000", "1,200,000", "85,000" };
                        for (int i = 0; i < cells.Length; i++)
                        {
                            row.ConstantItem((float)cells[i].wMm, Unit.Millimetre)
                               .Height(12, Unit.Millimetre)
                               .Border(0.5f)
                               .AlignMiddle().AlignRight().PaddingRight(2)
                               .Text(sampleValues[i]).FontSize(9);
                        }
                    });

                    col.Item().PaddingTop(8).Text("※ 実装では官製様式の全寸法（各欄の mm）をテンプレ化して描画する。")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return document.GeneratePdf();
    }
}
