using PayCalcDXPro.Reporting.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 支払明細書（給与・賞与）PDF をサーバ側（純.NET / SkiaSharp）で生成する。
/// 現行 R_給明細／R_賞明細（xlsxテンプレート）の固定セルレイアウトを踏襲：
/// ヘッダ（タイトル＋対象年月＋支給日/締日）→ 所属/社員番号/氏名 → 支給/控除/勤怠の5列グリッド → 年末調整/差引 → 備考。
/// 「A4に1明細」「A4に2明細」の2レイアウト。日本語は Windows 同梱フォントで解決（Windows配備前提）。
/// </summary>
public sealed class PaySlipPdfGenerator
{
    private const int Cols = 5;
    private static readonly string Gray = Colors.Grey.Lighten2;
    private static readonly string Line = Colors.Grey.Medium;
    private static readonly string LineLt = Colors.Grey.Lighten1;

    static PaySlipPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>複数人分を1つのPDFで返す。twoPerPage=true で A4に2明細。</summary>
    public byte[] Generate(IReadOnlyList<PaySlipModel> models, bool twoPerPage) =>
        Document.Create(container =>
        {
            if (!twoPerPage)
            {
                // 1明細はページ中央に配置し、上下余白を均等にして1ページ内のバランスを整える。
                foreach (var m in models)
                    container.Page(p => Page(p, c => c.AlignMiddle().Element(x => Block(x, m, compact: false))));
            }
            else
            {
                // A4に2明細（現行 R_給明細 sheet1）＝3カラム縦並びのコンパクト版を上下2人。
                // 用紙を横半分に切断したとき、各片で明細の上下余白が均等になるよう、
                // 上下マージン0で内容を2等分し、各半分の中央に明細を配置する。
                for (int i = 0; i < models.Count; i += 2)
                {
                    var a = models[i];
                    var b = i + 1 < models.Count ? models[i + 1] : null;
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.MarginVertical(0);
                        page.MarginHorizontal(1.2f, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo").FontSize(9));
                        float half = PageSizes.A4.Height / 2;
                        page.Content().Column(col =>
                        {
                            // 上半分（切断線＝中央に薄い罫線）。AlignMiddleで上下余白を均等化。
                            col.Item().Height(half).BorderBottom(0.5f).BorderColor(LineLt)
                                .PaddingVertical(6).AlignMiddle().Element(x => Block2(x, a));
                            // 下半分。
                            var lower = col.Item().Height(half).PaddingVertical(6).AlignMiddle();
                            if (b is not null) lower.Element(x => Block2(x, b));
                        });
                    });
                }
            }
        }).GeneratePdf();

    public byte[] Generate(PaySlipModel m) => Generate(new[] { m }, twoPerPage: false);

    private static void Page(PageDescriptor page, Action<IContainer> content)
    {
        page.Size(PageSizes.A4);
        page.Margin(1.2f, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo").FontSize(9));
        page.Content().Element(content);
    }

    private static void Block(IContainer container, PaySlipModel m, bool compact)
    {
        float fs = compact ? 8f : 9f;
        float gap = compact ? 10f : 22f;   // 表組み単位の間に入れる1行分の空白

        container.Column(col =>
        {
            // ===== ヘッダ：タイトル｜対象年月／支給日・締日 =====
            col.Item().Border(0.75f).BorderColor(Line).Row(row =>
            {
                row.ConstantItem(compact ? 150 : 175).Background(Gray).BorderRight(0.75f).BorderColor(Line)
                    .MinHeight(compact ? 30 : 38).AlignMiddle().Padding(4)
                    .Text(m.タイトル).Bold().FontSize(compact ? 11 : 13);
                row.RelativeItem().Column(c =>
                {
                    c.Item().PaddingVertical(2).AlignCenter().Text(m.対象表示).FontSize(compact ? 12 : 14).Bold();
                    c.Item().BorderTop(0.5f).BorderColor(LineLt).Padding(2).Row(rr =>
                    {
                        rr.RelativeItem().AlignCenter().Text($"支給日 ： {m.支給日表示}").FontSize(fs);
                        if (!string.IsNullOrEmpty(m.締日表示))
                            rr.RelativeItem().AlignCenter().Text($"締日 ： {m.締日表示}").FontSize(fs);
                    });
                });
            });

            col.Item().PaddingTop(2).AlignRight().Text(m.会社名).FontSize(fs + 1);

            // ===== 従業員No／氏名／所属（従業員No・氏名は狭く、所属を長く） =====
            col.Item().PaddingTop(2).Border(0.5f).BorderColor(Line).Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(compact ? 60 : 72); c.ConstantColumn(compact ? 110 : 132); c.RelativeColumn(); });
                Head(t, "従業員No", fs); Head(t, "氏名", fs); Head(t, "所属", fs);
                Val(t, m.社員番号, fs); Val(t, $"{m.従業員名}　様", fs); Val(t, m.所属, fs);
            });

            // ===== 支給 / 控除 / 勤怠（各表組みの間は1行分あける） =====
            col.Item().PaddingTop(gap).Element(c => Grid(c, "支給", m.支給.Cast<object>().ToList(), fs));
            col.Item().PaddingTop(gap).Element(c => Grid(c, "控除", m.控除.Cast<object>().ToList(), fs));
            if (m.勤怠.Count > 0)
                col.Item().PaddingTop(gap).Element(c => Grid(c, "勤怠", m.勤怠.Cast<object>().ToList(), fs));

            // ===== 年末調整＋差引支給額 =====
            col.Item().PaddingTop(gap).Row(row =>
            {
                row.ConstantItem(compact ? 240 : 280).Border(0.5f).BorderColor(Line).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(compact ? 34 : 42); c.RelativeColumn(); c.RelativeColumn(); });
                    t.Cell().RowSpan(2).Background(Gray).Border(0.25f).BorderColor(Line)
                        .AlignMiddle().AlignCenter().Text("年末\n調整").FontSize(fs - 1);
                    Head(t, "還付金", fs); Head(t, "徴収金", fs);
                    ValR(t, m.年末調整還付金 == 0 ? "" : m.年末調整還付金.ToString("N0"), fs);
                    ValR(t, m.年末調整徴収金 == 0 ? "" : m.年末調整徴収金.ToString("N0"), fs);
                });
                row.RelativeItem().PaddingLeft(8).Border(1.5f).BorderColor(Colors.Black).Padding(compact ? 5 : 7).Row(r =>
                {
                    r.RelativeItem().AlignMiddle().Text("差引支給額").FontSize(compact ? 11 : 13).Bold();
                    r.ConstantItem(compact ? 120 : 150).AlignRight().AlignMiddle()
                        .Text($"{m.差引支給額:N0}").FontSize(compact ? 14 : 16).Bold();
                });
            });

            // ===== 備考 =====
            col.Item().PaddingTop(gap).Border(0.5f).BorderColor(Line).Row(r =>
            {
                r.ConstantItem(compact ? 50 : 60).Background(Gray).BorderRight(0.25f).BorderColor(Line)
                    .AlignMiddle().AlignCenter().Text("備考").FontSize(fs);
                r.RelativeItem().MinHeight(compact ? 18 : 24).Padding(4).Text(m.備考).FontSize(fs);
            });
        });
    }

    // ============================================================
    // A4に2明細：3カラム縦並びのコンパクト版（現行 R_給明細 sheet1）
    // ============================================================
    private static void Block2(IContainer container, PaySlipModel m)
    {
        const float fs = 7.5f;
        container.Column(col =>
        {
            // ヘッダ：タイトル｜対象年月｜会社名。
            col.Item().Row(r =>
            {
                r.RelativeItem().AlignMiddle().Text(m.タイトル).Bold().FontSize(11);
                r.RelativeItem().AlignCenter().AlignMiddle().Text(m.対象表示).Bold().FontSize(11);
                r.RelativeItem().AlignRight().AlignMiddle().Text(m.会社名).FontSize(fs + 1);
            });
            // 従業員No／氏名／支給日 ＋ 所属（延長）／締日（支給日の真下）。
            col.Item().PaddingBottom(2).Border(0.5f).BorderColor(Line).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(44); c.RelativeColumn(); c.ConstantColumn(36); c.RelativeColumn();
                    c.ConstantColumn(44); c.RelativeColumn();
                });
                Head(t, "従業員No", fs); Val(t, m.社員番号, fs); Head(t, "氏名", fs); Val(t, $"{m.従業員名}　様", fs);
                Head(t, "支給日", fs); Val(t, m.支給日表示, fs);
                Head(t, "所属", fs);
                t.Cell().ColumnSpan(3).Border(0.25f).BorderColor(Line).Padding(3).Text(m.所属).FontSize(fs);
                Head(t, "締日", fs); Val(t, m.締日表示, fs);
            });
            // 3カラム本体：支給／控除／勤怠＋年末調整＋差引。所属行との間を半行分あける。
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Element(c => VList(c, "支給", m.支給, fs));
                row.ConstantItem(4);
                row.RelativeItem().Element(c => VList(c, "控除", m.控除, fs));
                row.ConstantItem(4);
                row.RelativeItem().Element(c => KList(c, m, fs));
            });
            // 備考。
            col.Item().PaddingTop(2).Border(0.5f).BorderColor(Line).Row(r =>
            {
                r.ConstantItem(34).Background(Gray).AlignMiddle().AlignCenter().Text("備考").FontSize(fs);
                r.RelativeItem().Padding(2).MinHeight(12).Text(m.備考).FontSize(fs);
            });
        });
    }

    /// <summary>支給／控除の縦リスト（名称左・金額右。合計は太字＋上罫線）。</summary>
    private static void VList(IContainer container, string label, IReadOnlyList<PaySlipLine> items, float fs)
    {
        container.Border(0.5f).BorderColor(Line).Column(col =>
        {
            col.Item().Background(Gray).Padding(2).Text(label).Bold().FontSize(fs);
            foreach (var it in items)
            {
                col.Item().BorderTop(it.合計 ? 0.75f : 0.25f).BorderColor(it.合計 ? Line : LineLt).Row(r =>
                {
                    r.RelativeItem().PaddingHorizontal(3).PaddingVertical(1).Text(it.名称).FontSize(fs);
                    var v = r.ConstantItem(64).PaddingHorizontal(3).PaddingVertical(1).AlignRight()
                        .Text(it.金額 is null ? "" : it.金額.Value.ToString("N0")).FontSize(fs);
                    if (it.合計) v.Bold();
                });
            }
        });
    }

    /// <summary>勤怠の縦リスト＋年末調整還付/徴収＋差引支給額。残業は (60h以下／60h超) の2値。
    /// 差引支給額が支給/控除の総額ライン（各列の末尾行）と揃うよう、勤怠の下にフィラー行を入れて行数を合わせる。</summary>
    private static void KList(IContainer container, PaySlipModel m, float fs)
    {
        // 年末調整(見出し+還付+徴収=3行)＋差引(1行)＝4行。支給/控除の項目数に合わせて勤怠下の空行数を決める。
        int 項目数 = Math.Max(m.支給.Count, m.控除.Count);
        int filler = Math.Max(0, 項目数 - m.勤怠.Count - 4);

        container.Border(0.5f).BorderColor(Line).Column(col =>
        {
            col.Item().Background(Gray).Padding(2).Text("勤怠").Bold().FontSize(fs);
            foreach (var f in m.勤怠)
            {
                col.Item().BorderTop(0.25f).BorderColor(LineLt).Row(r =>
                {
                    r.RelativeItem().PaddingHorizontal(3).PaddingVertical(1).Text(f.名称).FontSize(fs - 0.5f);
                    if (f.値2 is not null)
                    {
                        r.ConstantItem(34).PaddingVertical(1).AlignRight().Text(f.値).FontSize(fs);
                        r.ConstantItem(34).PaddingHorizontal(3).PaddingVertical(1).AlignRight().Text(f.値2).FontSize(fs);
                    }
                    else
                    {
                        r.ConstantItem(68).PaddingHorizontal(3).PaddingVertical(1).AlignRight().Text(f.値).FontSize(fs);
                    }
                });
            }
            // フィラー（差引支給額を総額ラインへ揃えるための空行）
            for (int i = 0; i < filler; i++)
                col.Item().BorderTop(0.25f).BorderColor(LineLt).PaddingVertical(1).Text(" ").FontSize(fs);

            // 年末調整（見出し＋還付＋徴収）
            col.Item().BorderTop(0.75f).BorderColor(Line).Background(Gray).Padding(2).Text("年末調整").Bold().FontSize(fs);
            col.Item().Row(r =>
            {
                r.RelativeItem().PaddingHorizontal(3).PaddingVertical(1).Text("還付金").FontSize(fs);
                r.ConstantItem(64).PaddingHorizontal(3).PaddingVertical(1).AlignRight()
                    .Text(m.年末調整還付金 == 0 ? "" : m.年末調整還付金.ToString("N0")).FontSize(fs);
            });
            col.Item().BorderTop(0.25f).BorderColor(LineLt).Row(r =>
            {
                r.RelativeItem().PaddingHorizontal(3).PaddingVertical(1).Text("徴収金").FontSize(fs);
                r.ConstantItem(64).PaddingHorizontal(3).PaddingVertical(1).AlignRight()
                    .Text(m.年末調整徴収金 == 0 ? "" : m.年末調整徴収金.ToString("N0")).FontSize(fs);
            });

            // 差引支給額（最終行＝総支給額/総控除額のラインに揃う。行高は他行と同じに保つ）
            col.Item().BorderTop(1f).BorderColor(Colors.Black).Row(r =>
            {
                r.RelativeItem().PaddingHorizontal(3).PaddingVertical(1).Text("差引支給額").Bold().FontSize(fs);
                r.ConstantItem(70).PaddingHorizontal(3).PaddingVertical(1).AlignRight()
                    .Text(m.差引支給額.ToString("N0")).Bold().FontSize(fs);
            });
        });
    }

    /// <summary>セクション（左に縦ラベル列＋5列の固定グリッド）。items は PaySlipLine または PaySlipFact。</summary>
    private static void Grid(IContainer container, string label, IReadOnlyList<object> items, float fs)
    {
        container.Border(0.5f).BorderColor(Line).Row(row =>
        {
            row.ConstantItem(26).Background(Gray).BorderRight(0.25f).BorderColor(Line)
                .AlignMiddle().AlignCenter().Text(label).Bold().FontSize(fs);
            row.RelativeItem().Table(t =>
            {
                t.ColumnsDefinition(c => { for (int i = 0; i < Cols; i++) c.RelativeColumn(); });
                // 行数を5の倍数に揃える（最終行を埋める）。
                int pad = (Cols - items.Count % Cols) % Cols;
                foreach (var it in items)
                {
                    if (it is PaySlipLine pl) MoneyCell(t, pl, fs);
                    else if (it is PaySlipFact pf) FactCell(t, pf, fs);
                }
                for (int i = 0; i < pad; i++) Blank(t);
            });
        });
    }

    private static void MoneyCell(QuestPDF.Fluent.TableDescriptor t, PaySlipLine pl, float fs)
    {
        if (string.IsNullOrEmpty(pl.名称) && pl.金額 is null) { Blank(t); return; }
        var cell = t.Cell().Border(pl.合計 ? 1.25f : 0.25f).BorderColor(pl.合計 ? Colors.Black : LineLt).Padding(3);
        cell.Column(c =>
        {
            c.Item().Text(pl.名称).FontSize(fs - 2.5f).FontColor(Colors.Grey.Darken1);
            var val = c.Item().AlignRight().Text(pl.金額 is null ? "" : pl.金額.Value.ToString("N0")).FontSize(fs);
            if (pl.合計) val.Bold();
        });
    }

    private static void FactCell(QuestPDF.Fluent.TableDescriptor t, PaySlipFact pf, float fs)
    {
        if (string.IsNullOrEmpty(pf.名称)) { Blank(t); return; }
        t.Cell().Border(0.25f).BorderColor(LineLt).Padding(3).Column(c =>
        {
            c.Item().Text(pf.名称).FontSize(fs - 2.5f).FontColor(Colors.Grey.Darken1);
            c.Item().AlignRight().Text(pf.値).FontSize(fs);
        });
    }

    private static void Blank(QuestPDF.Fluent.TableDescriptor t)
        => t.Cell().Border(0.25f).BorderColor(LineLt).MinHeight(26);

    private static void Head(QuestPDF.Fluent.TableDescriptor t, string text, float fs)
        => t.Cell().Background(Gray).Border(0.25f).BorderColor(Line).Padding(3).Text(text).FontSize(fs).Bold();

    private static void Val(QuestPDF.Fluent.TableDescriptor t, string text, float fs)
        => t.Cell().Border(0.25f).BorderColor(Line).Padding(3).Text(text).FontSize(fs);

    private static void ValR(QuestPDF.Fluent.TableDescriptor t, string text, float fs)
        => t.Cell().Border(0.25f).BorderColor(Line).Padding(3).AlignRight().Text(text).FontSize(fs);
}
