using PayCalcDXPro.Reporting.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayCalcDXPro.Reporting;

/// <summary>銀行振込一覧表（現行 R_銀行表）PDF。縦A4・全罫線。1ページ50人固定（不足は空行）・上下中央寄せ。</summary>
public sealed class BankTransferPdfGenerator
{
    private static readonly string Head = Colors.Grey.Lighten3;
    private static readonly string Line = Colors.Grey.Darken1;
    private const float FontPt = 8f;
    private const float GridW = 0.5f;
    /// <summary>1ページの明細行数（50人固定）。</summary>
    private const int PerPage = 50;
    /// <summary>明細セルの上下余白（行間）。50行＋見出し＋合計が1ページに収まる範囲で広めに。</summary>
    private const float RowPadV = 2.7f;

    static BankTransferPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(BankTransferModel m)
    {
        int pages = Math.Max(1, (int)Math.Ceiling(m.明細.Count / (double)PerPage));
        return Document.Create(c =>
        {
            for (int pg = 0; pg < pages; pg++)
            {
                int start = pg * PerPage;
                bool last = pg == pages - 1;
                c.Page(p =>
                {
                    p.Size(PageSizes.A4);
                    p.Margin(1.5f, Unit.Centimetre);
                    p.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo").FontSize(FontPt).LineHeight(1f));
                    p.Content().Element(x => Body(x, m, start, last));
                });
            }
        }).GeneratePdf();
    }

    private static void Body(IContainer c, BankTransferModel m, int start, bool last)
    {
        // 内容ブロックを上下中央寄せして余白をバランスさせる。
        c.ExtendVertical().AlignMiddle().Column(col =>
        {
            // 見出し
            col.Item().Row(r =>
            {
                r.RelativeItem().AlignBottom().Text(t =>
                {
                    t.Span("銀行振込一覧表").Bold().FontSize(15);
                    t.Span("　" + m.支給見出し).FontSize(9);
                });
                r.RelativeItem().AlignBottom().AlignRight().Text("支給日： " + m.支給日).FontSize(9);
            });

            col.Item().PaddingTop(4).Table(t =>
            {
                t.ColumnsDefinition(d =>
                {
                    d.RelativeColumn(2.4f);   // 銀行名
                    d.RelativeColumn(2.4f);   // 支店名
                    d.RelativeColumn(0.9f);   // 種類
                    d.RelativeColumn(1.7f);   // 口座番号
                    d.RelativeColumn(0.8f);   // No
                    d.RelativeColumn(2.2f);   // 口座名義人
                    d.RelativeColumn(2.2f);   // カナ
                    d.RelativeColumn(1.8f);   // 振込金額
                });

                Hc(t, "銀行名"); Hc(t, "支店名"); Hc(t, "種類"); Hc(t, "口座番号");
                Hc(t, "No"); Hc(t, "口座名義人"); Hc(t, "カナ"); Hc(t, "振込金額");

                // 50行固定（明細＋不足分は空行）
                for (int i = 0; i < PerPage; i++)
                {
                    int idx = start + i;
                    if (idx < m.明細.Count)
                    {
                        var e = m.明細[idx];
                        Lc(t, e.銀行名); Lc(t, e.支店名); Cc(t, e.種類); Lc(t, e.口座番号);
                        Cc(t, e.No); Lc(t, e.口座名義人); Lc(t, e.カナ); Vc(t, e.振込金額.ToString("#,0"));
                    }
                    else
                    {
                        for (int k = 0; k < 8; k++) Lc(t, "");
                    }
                }

                // 合計行（最終ページのみ。「合 計」を6列ぶち抜き、件数・金額を末尾2列に）
                if (last)
                {
                    t.Cell().ColumnSpan(6).Border(GridW).BorderColor(Line).Background(Head)
                        .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().AlignCenter().Text("合　計").FontSize(FontPt).Bold();
                    Vc(t, $"{m.合計件数}件", bold: true);
                    Vc(t, m.合計金額.ToString("#,0"), bold: true);
                }
            });
        });
    }

    private static void Hc(TableDescriptor t, string s)
        => t.Cell().Border(GridW).BorderColor(Line).Background(Head)
            .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().AlignCenter().Text(s).FontSize(FontPt).Bold();

    /// <summary>左寄せセル。</summary>
    private static void Lc(TableDescriptor t, string s)
        => t.Cell().Border(GridW).BorderColor(Line)
            .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().Text(s).FontSize(FontPt);

    /// <summary>中央寄せセル。</summary>
    private static void Cc(TableDescriptor t, string s)
        => t.Cell().Border(GridW).BorderColor(Line)
            .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().AlignCenter().Text(s).FontSize(FontPt);

    /// <summary>右寄せ（金額）セル。</summary>
    private static void Vc(TableDescriptor t, string s, bool bold = false)
    {
        var cell = t.Cell().Border(GridW).BorderColor(Line)
            .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().AlignRight();
        if (bold) cell.Background(Head).Text(s).FontSize(FontPt).Bold();
        else cell.Text(s).FontSize(FontPt);
    }
}
