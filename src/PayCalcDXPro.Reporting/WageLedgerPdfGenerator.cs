using PayCalcDXPro.Reporting.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 賃金台帳（支給日別）PDF。現行 xlsx テンプレート R_給台帳月／R_賞台帳月 のレイアウトに寄せて生成する。
/// 「項目（行）×従業員（列）」のクロス集計を罫線で再現し、ページ小計と全体合計の列を持つ。横A4。
/// </summary>
public sealed class WageLedgerPdfGenerator
{
    private static readonly string Head = Colors.Grey.Lighten3;
    private static readonly string Line = Colors.Grey.Darken1;

    /// <summary>1ページあたりの従業員（列）数。</summary>
    private const int PerPage = 8;

    /// <summary>セルの上下余白（行間・給与）。横罫線を省いた分、詰めている。</summary>
    private const float RowPadV = 0.6f;
    /// <summary>セルの上下余白（行間・賞与）。行数が少ないので広げ、1ページ内の上下バランスを取る。</summary>
    private const float RowPadVBonus = 1.7f;
    /// <summary>実際に使用する行間（給与/賞与で切替）。</summary>
    private float _padV = RowPadV;
    /// <summary>給与表と勤怠表の間隔（pt）。</summary>
    private const float TableGap = 4f;
    /// <summary>帳票タイトル行と会社名行の間隔（pt）。</summary>
    private const float TitleGap = 6f;
    /// <summary>本文フォントサイズ。</summary>
    private const float FontPt = 6.5f;
    /// <summary>縦罫線・外枠の太さ。</summary>
    private const float GridW = 0.4f;
    /// <summary>グループ境界の横罫線の太さ（xlsx テンプレに合わせ、行ごとの横罫線は引かない）。</summary>
    private const float GroupW = 0.4f;
    /// <summary>項目ラベル列の幅。</summary>
    private const float LabelW = 112f;
    /// <summary>小計・合計列の相対幅（データ列＝1 に対する倍率。データ列を狭く、小計/合計は広めに保つ）。</summary>
    private const float TotalWeight = 1.25f;

    static WageLedgerPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(WageLedgerModel m)
    {
        _padV = m.Is賞与 ? RowPadVBonus : RowPadV;
        int empCount = m.従業員.Count;
        int pages = Math.Max(1, (int)Math.Ceiling(empCount / (double)PerPage));

        return Document.Create(c =>
        {
            for (int pg = 0; pg < pages; pg++)
            {
                int start = pg * PerPage;
                int cnt = Math.Max(0, Math.Min(PerPage, empCount - start));
                c.Page(p =>
                {
                    p.Size(PageSizes.A4.Landscape());
                    p.MarginVertical(0.5f, Unit.Centimetre);
                    p.MarginHorizontal(1.5f, Unit.Centimetre);   // 表の左右余白
                    p.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo").FontSize(FontPt).LineHeight(1f));
                    p.Content().Element(x => PageBody(x, m, start, cnt));
                });
            }
        }).GeneratePdf();
    }

    private void PageBody(IContainer c, WageLedgerModel m, int start, int cnt)
    {
        // 勤怠項目（所定出勤日数以降＝金額でない行）を境に給与表と勤怠表へ分割する。
        int split = -1;
        for (int i = 0; i < m.行.Count; i++) { if (!m.行[i].金額) { split = i; break; } }
        var 給与行 = split < 0 ? m.行 : (IReadOnlyList<LedgerRow>)m.行.Take(split).ToList();
        var 勤怠行 = split < 0 ? Array.Empty<LedgerRow>() : (IReadOnlyList<LedgerRow>)m.行.Skip(split).ToList();

        // 内容ブロックを上下中央寄せして、ページ上下の余白を均等にする。
        c.ExtendVertical().AlignMiddle().Column(col =>
        {
            // ===== 見出し（賃金台帳［支給日別］ 部門 … 年）=====
            col.Item().Row(r =>
            {
                r.RelativeItem().AlignBottom().Text(t =>
                {
                    t.Span("賃金台帳").Bold().FontSize(13);
                    t.Span("　［支給日別］").FontSize(8);
                    if (!string.IsNullOrEmpty(m.部門)) t.Span("　" + m.部門).FontSize(8);
                });
                r.ConstantItem(90).AlignBottom().AlignRight().Text(m.年).FontSize(9);
            });
            col.Item().PaddingTop(TitleGap).Row(r =>
            {
                r.RelativeItem().AlignMiddle().Text(m.会社名).FontSize(9);
                r.RelativeItem().AlignCenter().AlignMiddle().Text(t =>
                {
                    t.Span(m.支給見出し).FontSize(9);
                    if (!m.Is賞与) t.Span("　給与").FontSize(9);   // 賞与は支給見出しに「賞与」を含むため付けない
                });
                r.RelativeItem().AlignRight().AlignMiddle().Text(t =>
                {
                    t.Span("支給日： " + m.支給日).FontSize(8);
                    if (!string.IsNullOrEmpty(m.締日)) t.Span("　" + m.締日).FontSize(8);
                });
            });

            // ===== 給与クロス集計表（見出し付き）=====
            col.Item().PaddingTop(3).Element(x => Table(x, m, start, cnt, 給与行, header: true));

            // ===== 勤怠クロス集計表（間隔をあけて分離。列幅は給与表と一致）=====
            if (勤怠行.Count > 0)
                col.Item().PaddingTop(TableGap).Element(x => Table(x, m, start, cnt, 勤怠行, header: false));
        });
    }

    /// <summary>従業員（列）×項目（行）のクロス集計表を1つ描画する。header=true でNo./氏名見出しを付ける。</summary>
    private void Table(IContainer c, WageLedgerModel m, int start, int cnt, IReadOnlyList<LedgerRow> rows, bool header)
    {
        c.Table(t =>
        {
            t.ColumnsDefinition(d =>
            {
                d.ConstantColumn(LabelW);                                  // 項目ラベル
                for (int i = 0; i < PerPage; i++) d.RelativeColumn(1f);    // 従業員（データ列＝狭め）
                d.RelativeColumn(TotalWeight);                            // 小計（広め）
                d.RelativeColumn(TotalWeight);                            // 合計（広め）
            });

            if (header)
            {
                // 見出し2行。No行と氏名行の間には横罫線を引かず（上端のみ／下端のみ）、
                // 氏名行の下に表との区切り罫線を引く。小計・合計は2行ぶち抜きで全周罫線。
                Hc(t, "No.", top: true, bottom: false);
                for (int i = 0; i < PerPage; i++) Hc(t, i < cnt ? m.従業員[start + i].No : "", top: true, bottom: false);
                Hc(t, "（小計）", top: true, bottom: true, rs: 2); Hc(t, "合 計", top: true, bottom: true, rs: 2);
                Hc(t, "氏名", top: false, bottom: true);
                for (int i = 0; i < PerPage; i++) Hc(t, i < cnt ? m.従業員[start + i].氏名 : "", top: false, bottom: true);
            }

            // 項目行：横罫線はグループ境界（太線）のみ。最終行に下罫線で表を閉じる。
            for (int idx = 0; idx < rows.Count; idx++)
            {
                var row = rows[idx];
                bool groupTop = row.太線;
                bool last = idx == rows.Count - 1;
                Lc(t, row.ラベル, groupTop, last, head: true);
                for (int i = 0; i < PerPage; i++)
                {
                    double? v = i < cnt ? row.値[start + i] : null;
                    Vc(t, Fmt(v, row.金額), groupTop, last);
                }
                // 小計＝当ページ従業員の合計、合計＝全従業員の合計
                double 小 = Sum(row.値, start, cnt);
                double 合 = Sum(row.値, 0, row.値.Count);
                Vc(t, Fmt(小, row.金額), groupTop, last); Vc(t, Fmt(合, row.金額), groupTop, last);
            }
        });
    }

    private static double Sum(IReadOnlyList<double?> vals, int start, int count)
    {
        double s = 0;
        for (int i = start; i < start + count && i < vals.Count; i++) s += vals[i] ?? 0;
        return s;
    }

    private static string Fmt(double? v, bool money)
        => (v ?? 0) == 0 ? "" : money ? ((long)Math.Round(v!.Value)).ToString("#,0") : v!.Value.ToString("0.##");

    // ---- セル ----
    // 横罫線は xlsx テンプレに合わせ、グループ境界（groupTop）と表の下端（last）だけに引く。
    // 縦罫線は全セルに引く（外枠＝端セルの左右罫線、表の上端＝見出しの罫線で閉じる）。

    /// <summary>見出しセル（グレー・中央）。縦罫線は常時、横罫線は top/bottom 指定で制御。</summary>
    private void Hc(TableDescriptor t, string s, bool top, bool bottom, uint cs = 1, uint rs = 1)
        => t.Cell().ColumnSpan(cs).RowSpan(rs)
            .BorderVertical(GridW).BorderTop(top ? GridW : 0f).BorderBottom(bottom ? GridW : 0f)
            .BorderColor(Line).Background(Head)
            .PaddingVertical(_padV).PaddingHorizontal(2).AlignMiddle().AlignCenter().Text(s).FontSize(FontPt).Bold();

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
