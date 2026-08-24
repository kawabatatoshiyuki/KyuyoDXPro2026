using PayCalcDXPro.Reporting.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayCalcDXPro.Reporting;

/// <summary>給与/賞与封筒印刷（現行 R_封筒／R_封筒Y）PDF。長3封筒（120×235mm）1名1ページ・縦/横。
/// 縦長封筒：明細書名／支給日／氏名 殿／会社名を中央寄せに縦積み。
/// 横長封筒：R_封筒Y.xlsx のレイアウトに準拠（左上に宛先＝No./部門/氏名、下部に灰色帯で 明細書名／支給日／会社名）。</summary>
public sealed class EnvelopePdfGenerator
{
    // 長3封筒の寸法（mm）
    private const float Cho3W = 120f;
    private const float Cho3H = 235f;

    static EnvelopePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EnvelopeModel m)
    {
        return Document.Create(c =>
        {
            foreach (var row in m.明細)
            {
                if (m.横) ComposeLandscape(c, m, row);
                else ComposePortrait(c, m, row);
            }
        }).GeneratePdf();
    }

    // ===== 縦長封筒（R_封筒） =====
    // 明細書名（上）→支給日→氏名 殿（中央）→会社名（最下行）を中央寄せ。
    private static void ComposePortrait(IDocumentContainer c, EnvelopeModel m, EnvelopeRow row)
    {
        c.Page(p =>
        {
            p.Size(Cho3W, Cho3H, Unit.Millimetre);
            p.MarginVertical(30, Unit.Millimetre);     // 上下余白30mm
            p.MarginHorizontal(12, Unit.Millimetre);
            p.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo"));

            p.Content().Column(col =>
            {
                col.Spacing(4);
                // 明細書名：横封筒と同じ24pt、全幅のグレー帯。
                col.Item().Background(Colors.Grey.Lighten2).PaddingVertical(3, Unit.Millimetre)
                    .AlignCenter().Text(m.明細書名).FontSize(24).Bold();
                col.Item().AlignCenter().Text(m.支給日).FontSize(11);
                col.Item().ExtendVertical().AlignMiddle().Column(mid =>
                {
                    mid.Spacing(6);
                    mid.Item().AlignCenter().Text("No." + row.No).FontSize(11);
                    if (!string.IsNullOrEmpty(row.部門))
                        mid.Item().AlignCenter().Text(row.部門).FontSize(12);
                    mid.Item().AlignCenter().Text(row.氏名 + " 殿").FontSize(16).Bold();
                });
            });
            // 会社名はページ最下行（フッター）に固定。
            p.Footer().AlignCenter().Text(m.会社名).FontSize(10);
        });
    }

    // ===== 横長封筒（R_封筒Y.xlsx 準拠） =====
    // 左上に宛先（No./部門/氏名 殿）を左寄せで縦積み、下部に全幅の灰色帯で 明細書名／支給日／会社名 を横並び。
    private static void ComposeLandscape(IDocumentContainer c, EnvelopeModel m, EnvelopeRow row)
    {
        c.Page(p =>
        {
            p.Size(Cho3H, Cho3W, Unit.Millimetre);     // 235×120mm
            p.MarginTop(16, Unit.Millimetre);          // 上余白
            p.MarginBottom(20, Unit.Millimetre);       // 下余白（帯の下）
            p.MarginHorizontal(0);
            p.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo"));

            // 宛先ブロック（左上・左寄せ）。
            p.Content().PaddingLeft(28, Unit.Millimetre).Column(addr =>
            {
                addr.Item().Text("No." + row.No).FontSize(12);
                if (!string.IsNullOrEmpty(row.部門))
                    addr.Item().Text(row.部門).FontSize(11);
                addr.Item().PaddingTop(4, Unit.Millimetre).Text(row.氏名 + " 殿").FontSize(16);
            });

            // 下部：灰色帯（全幅）を最下部に固定。明細書名（大）／支給日／会社名 を左寄せで横並び。
            p.Footer().PaddingHorizontal(24, Unit.Millimetre)
                .Background(Colors.Grey.Lighten2)
                .PaddingVertical(4, Unit.Millimetre).PaddingLeft(6, Unit.Millimetre)
                .Row(b =>
                {
                    b.ConstantItem(72, Unit.Millimetre).AlignMiddle().Text(m.明細書名).FontSize(24);
                    b.ConstantItem(38, Unit.Millimetre).AlignMiddle().Text(m.支給日).FontSize(14);
                    b.RelativeItem().AlignMiddle().Text(m.会社名).FontSize(14);
                });
        });
    }
}
