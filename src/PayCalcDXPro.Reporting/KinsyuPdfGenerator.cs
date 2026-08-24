using PayCalcDXPro.Reporting.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayCalcDXPro.Reporting;

/// <summary>現金支給金種表（現行 R_金種表）PDF。縦A4・全罫線。1ページ50人固定（不足は空行）・上下中央寄せ。</summary>
public sealed class KinsyuPdfGenerator
{
    private static readonly string Head = Colors.Grey.Lighten3;
    private static readonly string Line = Colors.Grey.Darken1;
    private const float FontPt = 8f;
    private const float GridW = 0.5f;
    private const int PerPage = 50;
    /// <summary>明細セルの上下余白（行間）。50行＋見出し2行＋合計が1ページに収まる範囲で広めに。</summary>
    private const float RowPadV = 2.2f;

    static KinsyuPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(KinsyuModel m)
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

    private static void Body(IContainer c, KinsyuModel m, int start, bool last)
    {
        c.ExtendVertical().AlignMiddle().Column(col =>
        {
            // 見出し
            col.Item().Row(r =>
            {
                r.RelativeItem().AlignBottom().Text(t =>
                {
                    t.Span("現金支給金種表").Bold().FontSize(15);
                    t.Span("　" + m.支給見出し).FontSize(9);
                });
                r.RelativeItem().AlignBottom().AlignRight().Text(t =>
                {
                    t.Line("支給日： " + m.支給日).FontSize(9);
                    t.Line(m.Include2000 ? "2,000円札含める" : "2,000円札含めない").FontSize(8);
                });
            });

            col.Item().PaddingTop(4).Table(t =>
            {
                t.ColumnsDefinition(d =>
                {
                    d.RelativeColumn(1.4f);   // No（TEST001等が折り返さない幅）
                    d.RelativeColumn(2.1f);   // 氏名
                    d.RelativeColumn(1.8f);   // 支払金額
                    for (int i = 0; i < 10; i++) d.RelativeColumn(1.0f);   // 金種内訳
                });

                // 見出し2行：No/氏名/支払金額は2行ぶち抜き、金種内訳は10列ぶち抜き＋各額面
                Hc(t, "No", rs: 2); Hc(t, "氏名", rs: 2); Hc(t, "支払金額", rs: 2);
                Hc(t, "金種内訳", cs: 10);
                foreach (var v in KinsyuModel.額面) Hc(t, v.ToString("#,0"));

                // 50行固定（明細＋不足分は空行）
                for (int i = 0; i < PerPage; i++)
                {
                    int idx = start + i;
                    if (idx < m.明細.Count)
                    {
                        var e = m.明細[idx];
                        Cc(t, e.No); Lc(t, e.氏名); Vc(t, e.支払金額.ToString("#,0"));
                        for (int k = 0; k < 10; k++) Vc(t, e.金種[k] == 0 ? "" : e.金種[k].ToString());
                    }
                    else
                    {
                        for (int k = 0; k < 13; k++) Lc(t, "");
                    }
                }

                // 合計行（最終ページのみ）
                if (last)
                {
                    t.Cell().ColumnSpan(2).Border(GridW).BorderColor(Line).Background(Head)
                        .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().AlignCenter().Text("合　計").FontSize(FontPt).Bold();
                    Vc(t, m.合計支払金額.ToString("#,0"), bold: true);
                    for (int k = 0; k < 10; k++) Vc(t, m.合計金種[k] == 0 ? "" : m.合計金種[k].ToString(), bold: true);
                }
            });
        });
    }

    private static void Hc(TableDescriptor t, string s, uint cs = 1, uint rs = 1)
        => t.Cell().ColumnSpan(cs).RowSpan(rs).Border(GridW).BorderColor(Line).Background(Head)
            .PaddingVertical(RowPadV).PaddingHorizontal(2).AlignMiddle().AlignCenter().Text(s).FontSize(FontPt).Bold();

    private static void Lc(TableDescriptor t, string s)
        => t.Cell().Border(GridW).BorderColor(Line)
            .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().Text(s).FontSize(FontPt);

    private static void Cc(TableDescriptor t, string s)
        => t.Cell().Border(GridW).BorderColor(Line)
            .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().AlignCenter().Text(s).FontSize(FontPt);

    private static void Vc(TableDescriptor t, string s, bool bold = false)
    {
        var cell = t.Cell().Border(GridW).BorderColor(Line)
            .PaddingVertical(RowPadV).PaddingHorizontal(3).AlignMiddle().AlignRight();
        if (bold) cell.Background(Head).Text(s).FontSize(FontPt).Bold();
        else cell.Text(s).FontSize(FontPt);
    }
}
