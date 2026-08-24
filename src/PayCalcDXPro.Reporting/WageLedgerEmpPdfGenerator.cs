using PayCalcDXPro.Reporting.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 賃金台帳（従業員別）PDF。現行 xlsx テンプレート R_給台帳従／R_賞台帳従 に寄せて生成する。
/// 従業員1人を1ページとし、「項目（行）×支給回（列）」のクロス集計に年間合計列を持つ。横A4。
/// 罫線・グループ区切り・勤怠分離は支給日別（<see cref="WageLedgerPdfGenerator"/>）と同方針。
/// </summary>
public sealed class WageLedgerEmpPdfGenerator
{
    private static readonly string Head = Colors.Grey.Lighten3;
    private static readonly string Line = Colors.Grey.Darken1;

    private const float FontPt = 6.5f;
    private const float GridW = 0.4f;
    private const float GroupW = 0.4f;
    private const float LabelW = 112f;
    private const float TotalWeight = 1.25f;
    private const float TableGap = 4f;
    private const float TitleGap = 6f;
    private const float RowPadV = 0.6f;
    private const float RowPadVBonus = 1.7f;
    /// <summary>見出し行の上下余白（データ行より広げて窮屈さを解消）。</summary>
    private const float HeaderPadV = 3f;
    private float _padV = RowPadV;

    static WageLedgerEmpPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(WageLedgerEmpModel m)
    {
        _padV = m.Is賞与 ? RowPadVBonus : RowPadV;
        return Document.Create(c =>
        {
            foreach (var page in m.ページ)
            {
                c.Page(p =>
                {
                    p.Size(PageSizes.A4.Landscape());
                    p.MarginVertical(0.5f, Unit.Centimetre);
                    p.MarginHorizontal(1.5f, Unit.Centimetre);
                    p.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo").FontSize(FontPt).LineHeight(1f));
                    p.Content().Element(x => PageBody(x, m, page));
                });
            }
        }).GeneratePdf();
    }

    private void PageBody(IContainer c, WageLedgerEmpModel m, WageLedgerEmpPage page)
    {
        // 勤怠項目（所定出勤日数以降＝金額でない行）を境に給与表と勤怠表へ分割する。
        int split = -1;
        for (int i = 0; i < page.行.Count; i++) { if (!page.行[i].金額) { split = i; break; } }
        var 主行 = split < 0 ? page.行 : (IReadOnlyList<LedgerRow>)page.行.Take(split).ToList();
        var 勤怠行 = split < 0 ? Array.Empty<LedgerRow>() : (IReadOnlyList<LedgerRow>)page.行.Skip(split).ToList();

        c.ExtendVertical().AlignMiddle().Column(col =>
        {
            // ===== 見出し =====
            col.Item().Row(r =>
            {
                r.RelativeItem().AlignBottom().Text(t =>
                {
                    t.Span("賃金台帳").Bold().FontSize(13);
                    t.Span("　［従業員別］").FontSize(8);
                });
                r.ConstantItem(90).AlignBottom().AlignRight().Text(m.年).FontSize(9);
            });
            col.Item().PaddingTop(TitleGap).Row(r =>
            {
                r.RelativeItem().AlignMiddle().Text(m.会社名).FontSize(9);
                r.RelativeItem(2f).AlignCenter().AlignMiddle().Text(t =>
                {
                    t.Span("所属： ").FontSize(8); t.Span(page.所属).FontSize(8);
                    t.Span("　　社員NO: ").FontSize(8); t.Span(page.社員NO).FontSize(8);
                    t.Span("　　氏名: ").FontSize(8); t.Span(page.氏名).FontSize(8);
                });
                r.RelativeItem().AlignRight().AlignMiddle().Text(page.締日).FontSize(8);
            });

            col.Item().PaddingTop(3).Element(x => Table(x, m, page, 主行, header: true));
            if (勤怠行.Count > 0)
                col.Item().PaddingTop(TableGap).Element(x => Table(x, m, page, 勤怠行, header: false));
        });
    }

    private void Table(IContainer c, WageLedgerEmpModel m, WageLedgerEmpPage page, IReadOnlyList<LedgerRow> rows, bool header)
    {
        int n = page.列.Count;
        c.Table(t =>
        {
            t.ColumnsDefinition(d =>
            {
                d.ConstantColumn(LabelW);                            // 項目ラベル／区分
                for (int i = 0; i < n; i++) d.RelativeColumn(1f);    // 支給回（月／賞与回）
                d.RelativeColumn(TotalWeight);                       // 合計
            });

            if (header)
            {
                // 表頭1行：区分ラベル｜各列(上段＝支給年月/賞与回, 下段＝支給日)｜合計
                Hc(t, m.区分ラベル);
                foreach (var c2 in page.列) Hc2(t, c2.上段, c2.下段);
                Hc(t, "合 計");
            }

            for (int idx = 0; idx < rows.Count; idx++)
            {
                var row = rows[idx];
                bool groupTop = row.太線;
                bool last = idx == rows.Count - 1;
                Lc(t, row.ラベル, groupTop, last, head: true);
                for (int i = 0; i < n; i++)
                {
                    double? v = i < row.値.Count ? row.値[i] : null;
                    Vc(t, Fmt(v, row.金額), groupTop, last);
                }
                double 合 = Sum(row.値);
                Vc(t, Fmt(合, row.金額), groupTop, last);
            }
        });
    }

    private static double Sum(IReadOnlyList<double?> vals)
    {
        double s = 0;
        for (int i = 0; i < vals.Count; i++) s += vals[i] ?? 0;
        return s;
    }

    private static string Fmt(double? v, bool money)
        => (v ?? 0) == 0 ? "" : money ? ((long)Math.Round(v!.Value)).ToString("#,0") : v!.Value.ToString("0.##");

    // ---- セル（横罫線はグループ境界・表の下端のみ。縦罫線は常時）----
    /// <summary>見出しセル（1行）。全周罫線。</summary>
    private void Hc(TableDescriptor t, string s)
        => t.Cell().Border(GridW).BorderColor(Line).Background(Head)
            .PaddingVertical(HeaderPadV).PaddingHorizontal(2).AlignMiddle().AlignCenter().Text(s).FontSize(FontPt).Bold();

    /// <summary>見出しセル（2段：上段＝支給年月/賞与回、下段＝支給日）。全周罫線・天地中央。</summary>
    private void Hc2(TableDescriptor t, string top, string bottom)
        => t.Cell().Border(GridW).BorderColor(Line).Background(Head)
            .PaddingVertical(HeaderPadV).PaddingHorizontal(2).AlignMiddle()
            .Column(col =>
            {
                col.Item().AlignCenter().Text(top).FontSize(FontPt).Bold();
                col.Item().AlignCenter().Text(bottom).FontSize(FontPt - 0.5f);
            });

    /// <summary>項目ラベルセル（左寄せ・薄グレー）。</summary>
    private void Lc(TableDescriptor t, string s, bool groupTop, bool last, bool head)
        => t.Cell().BorderVertical(GridW).BorderTop(groupTop ? GroupW : 0f).BorderBottom(last ? GridW : 0f)
            .BorderColor(Line).Background(head ? Head : Colors.White)
            .PaddingVertical(_padV).PaddingHorizontal(3).AlignMiddle().Text(s).FontSize(FontPt);

    /// <summary>数値セル（右寄せ）。</summary>
    private void Vc(TableDescriptor t, string s, bool groupTop, bool last)
        => t.Cell().BorderVertical(GridW).BorderTop(groupTop ? GroupW : 0f).BorderBottom(last ? GridW : 0f)
            .BorderColor(Line)
            .PaddingVertical(_padV).PaddingHorizontal(3).AlignMiddle().AlignRight().Text(s).FontSize(FontPt);
}
