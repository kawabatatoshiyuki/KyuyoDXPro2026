using PayCalcDXPro.Reporting.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayCalcDXPro.Reporting;

/// <summary>
/// 台帳PDF（導入設定台帳・従業員台帳）を、現行 xlsx テンプレート（R_導入設定台帳／R_従業員台帳）の
/// レイアウトに寄せて生成する。罫線フォームを QuestPDF で再現する。日本語は Windows 同梱フォントで解決。
/// </summary>
public sealed class LedgerPdfGenerator
{
    private static readonly string Head = Colors.Grey.Lighten3;
    private static readonly string Line = Colors.Grey.Darken1;

    static LedgerPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ============================================================ 従業員台帳
    public byte[] GenerateEmployees(IReadOnlyList<EmpLedgerModel> list) =>
        Document.Create(c =>
        {
            foreach (var m in list)
                c.Page(p =>
                {
                    p.Size(PageSizes.A4);
                    p.Margin(1.0f, Unit.Centimetre);
                    p.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo").FontSize(9));
                    p.Content().Element(x => EmpPage(x, m));
                });
        }).GeneratePdf();

    private static void EmpPage(IContainer c, EmpLedgerModel m)
    {
        c.Column(outer =>
        {
        outer.Item().Border(0.8f).BorderColor(Line).Column(col =>
        {
            // 見出し＋氏名/性別/生年月日/従業員No（氏名は1行・見出し列は同じ高さに揃う）
            col.Item().Row(r =>
            {
                r.ConstantItem(120).BorderRight(0.8f).BorderColor(Line).AlignMiddle().AlignCenter()
                    .Text("従業員台帳").Bold().FontSize(15);
                r.RelativeItem().Table(t =>
                {
                    t.ColumnsDefinition(d => { d.RelativeColumn(); d.ConstantColumn(40); d.ConstantColumn(82); d.ConstantColumn(64); });
                    H(t, "氏名"); H(t, "性別"); H(t, "生年月日"); H(t, "従業員No");
                    t.Cell().Border(0.4f).BorderColor(Line).PaddingVertical(7).PaddingHorizontal(3).AlignMiddle()
                        .Text(m.氏名 + (string.IsNullOrEmpty(m.氏名かな) ? "" : $"（{m.氏名かな}）")).FontSize(9).LineHeight(1f);
                    VC(t, m.性別); VC(t, m.生年月日); V(t, m.従業員No);
                });
            });

            // 住所 / 勤務の状況
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { d.RelativeColumn(3); d.RelativeColumn(2); });
                H(t, "住所"); H(t, "勤務の状況");
                V(t, (string.IsNullOrWhiteSpace(m.郵便番号) ? "" : $"〒{m.郵便番号}  ") + m.住所); V(t, m.勤務の状況);
            });

            // 中途就職退職年月日 / 事由（上段＝見出し、下段＝データ。事由はデータ前側＋手書き用（）枠）
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { d.ConstantColumn(140); d.RelativeColumn(); });
                H(t, "中途就職退職年月日"); H(t, "事由　※（）は手書きで追記してください");
                VC(t, m.中途就職退職年月日);
                V(t, (string.IsNullOrEmpty(m.事由) ? "" : m.事由 + "　") + "（　　　　　　　　　　　　　　　）");
            });

            // 所属
            Band(col, "所属", x => x.Table(t =>
            {
                t.ColumnsDefinition(d => { d.RelativeColumn(); d.RelativeColumn(); });
                H(t, "部門"); H(t, "職名"); V(t, m.部門); V(t, m.職名);
            }));

            // 所得税（本人／配偶者 ＋ 扶養者／特定親族）
            Band(col, "所得税", inner => inner.Column(ic =>
            {
                ic.Item().Table(t =>
                {
                    t.ColumnsDefinition(d => { for (int i = 0; i < 7; i++) d.RelativeColumn(); });
                    H(t, "本人情報", 4); H(t, "配偶者情報", 3);
                    H(t, "源泉所得税"); H(t, "障害者区分"); H(t, "寡婦・ひとり親"); H(t, "勤労学生");
                    H(t, "配偶者区分"); H(t, "障害者区分"); H(t, "合計所得(見積)");
                    V(t, m.源泉所得税); V(t, m.本人障害者); V(t, m.寡婦ひとり親); V(t, m.勤労学生);
                    V(t, m.配偶者区分); V(t, m.配偶者障害者); V(t, m.配偶者合計所得);
                });
                ic.Item().Table(t =>
                {
                    t.ColumnsDefinition(d => { for (int i = 0; i < 7; i++) d.RelativeColumn(); });
                    H(t, "扶養者情報", 6); H(t, "特定親族");
                    H(t, "扶養親族数"); H(t, "内同居親等"); H(t, "内老人扶養"); H(t, "内特定扶養"); H(t, "内特別障害"); H(t, "内一般障害");
                    VC(t, m.特定親族, 1, 2);   // 値行2行ぶち抜き＝内部罫線なし・上下中央
                    VC(t, m.扶養親族数); VC(t, m.内同居親等); VC(t, m.内老人扶養); VC(t, m.内特定扶養); VC(t, m.内特別障害); VC(t, m.内一般障害);
                });
            }));

            // 住民税
            Band(col, "住民税", x => x.Table(t =>
            {
                t.ColumnsDefinition(d => { d.RelativeColumn(); d.RelativeColumn(); d.RelativeColumn(); });
                H(t, "1月〜5月（前期）"); H(t, "6月"); H(t, "7月〜12月");
                VR(t, m.住民税前期); VR(t, m.住民税6月); VR(t, m.住民税後期);
            }));

            // 社会保険（報酬月額＝金額右寄せ／加入区分＝中央）
            Band(col, "社会保険", x => x.Table(t =>
            {
                t.ColumnsDefinition(d => { for (int i = 0; i < 7; i++) d.RelativeColumn(); });
                H(t, "報酬月額"); H(t, "健康保険"); H(t, "介護保険"); H(t, "子育支援"); H(t, "厚生年金"); H(t, "年金基金"); H(t, "雇用保険");
                VR(t, m.報酬月額); VC(t, m.健康保険); VC(t, m.介護保険); VC(t, m.子育支援); VC(t, m.厚生年金); VC(t, m.年金基金); VC(t, m.雇用保険);
            }));

            // 残業単価（テンプレ忠実：法定内＋法定外［上段60h以下／下段60h超］）
            Band(col, "残業単価", x => x.Table(t =>
            {
                t.ColumnsDefinition(d => { for (int i = 0; i < 5; i++) d.RelativeColumn(); });
                // 見出し2行（法定内は縦2行ぶち抜き／法定外は4列ぶち抜き）
                H(t, "法定内残業単価", 1, 2);
                H(t, "法定外残業単価（割増賃金）　上段：60h以下／下段：60h超", 4);
                H(t, "普通残業単価"); H(t, "深夜残業単価"); H(t, "休日出勤単価"); H(t, "休日深夜単価");
                // 値2行（法定内は縦2行ぶち抜き）
                VR(t, m.法定内, 1, 2);
                VR(t, m.普通); VR(t, m.深夜); VR(t, m.休日出勤); VR(t, m.休日深夜);
                VR(t, m.普通超); VR(t, m.深夜超); VR(t, m.休日出勤超); VR(t, m.休日深夜超);
            }));

            // 支給方法
            Band(col, "支給方法", x => x.Table(t =>
            {
                t.ColumnsDefinition(d => { d.ConstantColumn(60); d.ConstantColumn(60); d.RelativeColumn(); d.ConstantColumn(90); });
                H(t, "現金支給"); H(t, "口座振込"); H(t, "支給口座情報"); H(t, "日給単価");
                VC(t, m.現金支給); VC(t, m.口座振込); V(t, m.支給口座情報); VR(t, m.日給単価);
            }));

        });

            // 備考欄（表とは分離した独立枠。残り高さを埋めてページの上下余白を均等にする）
            outer.Item().PaddingTop(8).Extend().MinHeight(44).Border(0.8f).BorderColor(Line).Row(r =>
            {
                r.ConstantItem(64).BorderRight(0.4f).BorderColor(Line).Background(Head).AlignMiddle().AlignCenter()
                    .Text("備考欄").FontSize(8.5f).Bold();
                r.RelativeItem().Padding(4).AlignTop().Text(m.備考).FontSize(9);
            });
        });
    }

    /// <summary>左に縦見出し、右に内容を置く1バンド（従業員台帳の各区分）。</summary>
    private static void Band(ColumnDescriptor col, string label, Action<IContainer> content)
        => col.Item().Row(r =>
        {
            r.ConstantItem(64).Border(0.4f).BorderColor(Line).Background(Head).MinHeight(32).AlignMiddle().AlignCenter()
                .Text(label).FontSize(8.5f).Bold();
            r.RelativeItem().Element(content);
        });

    // ============================================================ 導入設定台帳（R_導入設定台帳：横A4・3ページ）
    public byte[] GenerateSetup(SetupLedgerModel m) =>
        Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Size(PageSizes.A4.Landscape());
                p.Margin(0.8f, Unit.Centimetre);
                p.DefaultTextStyle(x => x.FontFamily("Yu Gothic", "MS Gothic", "Meiryo").FontSize(8));
                p.Header().Element(x => SetupHeader(x, m));
                p.Content().PaddingTop(4).Element(x => SetupBody(x, m));
                p.Footer().AlignRight().Text(t => { t.CurrentPageNumber().FontSize(8); t.Span(" / ").FontSize(8); t.TotalPages().FontSize(8); });
            });
        }).GeneratePdf();

    private static void SetupHeader(IContainer c, SetupLedgerModel m)
    {
        c.Column(col =>
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().AlignMiddle().Text(m.会社名).FontSize(10);
                r.RelativeItem().AlignCenter().AlignMiddle().Text("導入設定台帳").Bold().FontSize(15);
                r.RelativeItem().AlignRight().AlignMiddle().Text(m.日付).FontSize(10);
            });
            col.Item().PaddingTop(3).LineHorizontal(1).LineColor(Line);
        });
    }

    private static void SetupBody(IContainer c, SetupLedgerModel m)
    {
        c.Column(col =>
        {
            // ===== ページ1：会社基本情報（会社情報＋部門＋役職）。余白は上下中央寄せで均等化 =====
            col.Item().ExtendVertical().AlignMiddle().Column(cc =>
            {
                BandTitle(cc, "会社基本情報");
                cc.Item().Element(x => CompanyForm(x, m));
                BandTitle(cc, "部門情報");
                cc.Item().Element(x => SlotGrid5(x, "部門名", m.部門));
                BandTitle(cc, "役職情報");
                cc.Item().Element(x => SlotGrid5(x, "役職名", m.役職));
            });

            // ===== ページ2：銀行（左）＋提出先（右）。サイド見出しなし・上下中央寄せで余白均等 =====
            col.Item().PageBreak();
            col.Item().ExtendVertical().AlignMiddle().Row(r =>
            {
                r.RelativeItem().Column(cc =>
                {
                    BandTitle(cc, "給与賞与振込先 銀行・支店名情報");
                    cc.Item().Element(x => SlotGrid2(x, "銀行名", "支店名", m.銀行));
                });
                r.ConstantItem(10);
                r.RelativeItem().Column(cc =>
                {
                    BandTitle(cc, "年末調整報告書 提出先情報");
                    cc.Item().Element(x => SlotGrid2(x, "市区町村名", "徴収義務者番号", m.提出先));
                });
            });

            // ===== ページ3：給与・賞与支給日（左）＋支給控除（右）。上下中央寄せで余白均等 =====
            col.Item().PageBreak();
            col.Item().ExtendVertical().AlignMiddle().Row(r =>
            {
                // 左：支給日情報（給与支給日＝左／賞与支給日＝右。いずれも行見出し）
                r.RelativeItem(5).Column(cc =>
                {
                    BandTitle(cc, "支給日情報");
                    cc.Item().Row(rr =>
                    {
                        // 給与支給日（行見出し）
                        rr.RelativeItem(7).Column(gc =>
                        {
                            SubTitle(gc, "給与支給日");
                            gc.Item().Table(t =>
                            {
                                t.ColumnsDefinition(d => { d.ConstantColumn(40); d.RelativeColumn(); d.ConstantColumn(40); d.RelativeColumn(); });
                                Hc(t, "締め日"); Vc(t, $"{m.締月} {m.締日付}締め"); Hc(t, "支給日"); VcC(t, m.基準支給日);
                            });
                            gc.Item().Table(t =>
                            {
                                t.ColumnsDefinition(d => { d.ConstantColumn(40); d.RelativeColumn(1); d.RelativeColumn(1.3f); d.ConstantColumn(50); });
                                Hc(t, "支給回"); Hc(t, "支給日"); Hc(t, "計算対象労働月"); Hc(t, "所定出勤日数");
                                foreach (var (kai, sikyu, taisho, shotei) in m.月別) { VcC(t, kai); VcC(t, sikyu); VcC(t, taisho); VcC(t, shotei); }
                            });
                        });
                        rr.ConstantItem(8);
                        // 賞与支給日（行見出し・固定4回枠）
                        rr.RelativeItem(3).Column(bc =>
                        {
                            SubTitle(bc, "賞与支給日");
                            bc.Item().Table(t =>
                            {
                                t.ColumnsDefinition(d => { d.ConstantColumn(40); d.RelativeColumn(); });
                                Hc(t, "賞与回"); Hc(t, "支給日");
                                for (int i = 0; i < 4; i++)
                                {
                                    VcC(t, $"第{i + 1}回");
                                    VcC(t, i < m.賞与.Count ? m.賞与[i].支給 : "");
                                }
                            });
                        });
                    });
                });
                r.ConstantItem(10);
                // 右：支給控除情報（給与・賞与を左右に。行見出し＋表の高さを高い方に揃える）
                r.RelativeItem(7).Column(cc =>
                {
                    BandTitle(cc, "支給控除情報");

                    var 給与支給 = Concat("基本給", m.給与手当.Select(a => a.名称 + (a.非課税 ? "（非）" : "")), new[] { "時間外手当", "勤怠控除", "課税通勤費", "非課税通勤費", "総支給額" });
                    var 給与控除 = Concat2(new[] { "健康保険", "介護保険", "子育支援", "厚生年金", "年金基金", "小規模共済", "雇用保険", "社会保険合計", "課税対象額", "所得税", "住民税" }, m.給与控除, new[] { "総控除額", "差引支給額" });
                    var 賞与支給 = Concat("賞与", m.賞与手当, new[] { "総支給額" });
                    var 賞与控除 = Concat2(new[] { "健康保険", "介護保険", "子育支援", "厚生年金", "年金基金", "小規模共済", "雇用保険", "社会保険合計", "課税対象額", "所得税" }, m.賞与控除, new[] { "総控除額", "差引支給額" });

                    // 本体行数を両ブロックの最大に揃える（表の高さ均等化＝高い方に合わせる）
                    int bodyRows = Math.Max(Math.Max(給与支給.Count - 1, 給与控除.Count - 2),
                                            Math.Max(賞与支給.Count - 1, 賞与控除.Count - 2));

                    cc.Item().Row(rr =>
                    {
                        rr.RelativeItem().Element(x => PayItemBlock(x, "給与支給控除項目情報", 給与支給, 給与控除, bodyRows));
                        rr.ConstantItem(8);
                        rr.RelativeItem().Element(x => PayItemBlock(x, "賞与支給控除項目情報", 賞与支給, 賞与控除, bodyRows));
                    });
                });
            });
        });
    }

    /// <summary>会社情報フォーム（会社名/所在地/代表者/連絡者の4列＋事業所名行＋税務署等9列）。</summary>
    private static void CompanyForm(IContainer c, SetupLedgerModel m)
    {
        c.Column(col =>
        {
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { d.RelativeColumn(1.3f); d.RelativeColumn(1.8f); d.RelativeColumn(1.0f); d.RelativeColumn(1.0f); });
                Hc(t, "会社名"); Hc(t, "所在地"); Hc(t, "代表者名"); Hc(t, "連絡者名");
                Vc(t, m.会社名カナ); Vc(t, m.所在地カナ); Vc(t, m.代表者氏名カナ); Vc(t, m.連絡者名カナ);
                Vc(t, m.会社名); Vc(t, m.所在地); Vc(t, m.代表者名); Vc(t, m.連絡者名);
            });
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { d.RelativeColumn(2); d.RelativeColumn(1); d.RelativeColumn(1); d.RelativeColumn(2); });
                Hc(t, "事業所名"); Hc(t, "電話番号"); Hc(t, "事業種目"); Hc(t, "事業所のマイナンバー");
                Vc(t, m.事業所名); Vc(t, m.電話番号); Vc(t, m.事業種目); Vc(t, $"{m.区分}　{m.マイナンバー}");
            });
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { for (int i = 0; i < 9; i++) d.RelativeColumn(); });
                Hc(t, "所轄税務署名"); Hc(t, "署番号"); Hc(t, "整理番号");
                Hc(t, "給与の支払方法\n及びその期日"); Hc(t, "支払金融機関"); Hc(t, "金融機関所在地");
                Hc(t, "労働保険\n適用事業所"); Hc(t, "年末調整\nの方法"); Hc(t, "残業手当計算\nの端数処理");
                Vc(t, m.税務署名); Vc(t, m.署番号); Vc(t, m.整理番号);
                Vc(t, m.支払方法); Vc(t, m.支払金融機関); Vc(t, m.金融機関所在地);
                Vc(t, m.労働保険); Vc(t, m.年末調整方法); Vc(t, m.残業端数);
            });
        });
    }

    /// <summary>5列×10行＝50枠のNO付きグリッド（部門・役職）。NO/名称のヘッダ行付き。番号は列方向に01-50。</summary>
    private static void SlotGrid5(IContainer c, string nameHdr, IReadOnlyList<string> names)
    {
        const int cols = 5, rows = 10;
        c.Border(0.5f).BorderColor(Line).Table(t =>
        {
            t.ColumnsDefinition(d => { for (int i = 0; i < cols; i++) { d.ConstantColumn(22); d.RelativeColumn(); } });
            for (int i = 0; i < cols; i++) { Hc(t, "NO"); Hc(t, nameHdr); }
            for (int r = 0; r < rows; r++)
                for (int cl = 0; cl < cols; cl++)
                {
                    int idx = cl * rows + r;
                    t.Cell().Border(0.25f).BorderColor(Line).Background(Head).PaddingVertical(0.8f).AlignCenter().AlignMiddle().Text((idx + 1).ToString("00")).FontSize(6.5f);
                    t.Cell().Border(0.25f).BorderColor(Line).PaddingVertical(0.8f).PaddingHorizontal(3).MinHeight(9).AlignMiddle().Text(idx < names.Count ? names[idx] : "").FontSize(7f);
                }
        });
    }

    /// <summary>2群×25行＝50枠のNO付きグリッド（銀行・提出先）。番号は群ごとに01-25/26-50。</summary>
    private static void SlotGrid2(IContainer c, string h1, string h2, IReadOnlyList<(string, string)> data)
    {
        const int rows = 25;
        c.Border(0.5f).BorderColor(Line).Table(t =>
        {
            t.ColumnsDefinition(d => { for (int g = 0; g < 2; g++) { d.ConstantColumn(22); d.RelativeColumn(); d.RelativeColumn(); } });
            Hc(t, "No"); Hc(t, h1); Hc(t, h2); Hc(t, "No"); Hc(t, h1); Hc(t, h2);
            for (int r = 0; r < rows; r++)
                for (int g = 0; g < 2; g++)
                {
                    int idx = g * rows + r;
                    var (c1, c2) = idx < data.Count ? data[idx] : ("", "");
                    t.Cell().Border(0.25f).BorderColor(Line).Background(Head).PaddingVertical(1.5f).AlignCenter().AlignMiddle().Text((idx + 1).ToString("00")).FontSize(7);
                    t.Cell().Border(0.25f).BorderColor(Line).PaddingVertical(1.5f).PaddingHorizontal(3).MinHeight(13).AlignMiddle().Text(c1).FontSize(7.5f);
                    t.Cell().Border(0.25f).BorderColor(Line).PaddingVertical(1.5f).PaddingHorizontal(3).MinHeight(13).AlignMiddle().Text(c2).FontSize(7.5f);
                }
        });
    }

    /// <summary>
    /// 給与/賞与の支給控除項目ブロック（行見出し＋支給項目|控除項目の2列）。
    /// 末尾の合計行（総支給額／総控除額・差引支給額）を同一行に揃える。
    /// bodyRows で本体行数を外部から指定し、給与・賞与ブロックの高さを揃える。
    /// </summary>
    private static void PayItemBlock(IContainer c, string title, List<string> 支給, List<string> 控除, int bodyRows)
    {
        // 末尾の合計行を分離（支給：総支給額／控除：総控除額・差引支給額）
        var sBody = 支給.Take(支給.Count - 1).ToList();
        var sTotal = 支給[支給.Count - 1];
        var kBody = 控除.Take(控除.Count - 2).ToList();
        var kTotal = 控除[控除.Count - 2];
        var kNet = 控除[控除.Count - 1];

        c.Column(col =>
        {
            SubTitle(col, title);   // 行見出し
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { d.RelativeColumn(); d.RelativeColumn(); });
                Hc(t, "支給項目"); Hc(t, "控除項目");
                for (int i = 0; i < bodyRows; i++)
                {
                    ItemCell(t, i < sBody.Count ? sBody[i] : "");
                    ItemCell(t, i < kBody.Count ? kBody[i] : "");
                }
                ItemCell(t, sTotal); ItemCell(t, kTotal);   // 総支給額｜総控除額（同一行）
                ItemCell(t, ""); ItemCell(t, kNet);          // （空）｜差引支給額
            });
        });
    }

    private static void ItemCell(TableDescriptor t, string s)
        => t.Cell().Border(0.25f).BorderColor(Line).PaddingVertical(2).PaddingHorizontal(3).MinHeight(11).AlignMiddle().Text(s).FontSize(8);

    private static List<string> Concat(string head, IEnumerable<string> mid, IEnumerable<string> tail)
    { var l = new List<string> { head }; l.AddRange(mid); l.AddRange(tail); return l; }
    private static List<string> Concat2(IEnumerable<string> a, IEnumerable<string> b, IEnumerable<string> c)
    { var l = new List<string>(a); l.AddRange(b); l.AddRange(c); return l; }

    /// <summary>全幅のグレー帯セクション見出し（テンプレ準拠）。</summary>
    private static void BandTitle(ColumnDescriptor col, string text)
        => col.Item().PaddingTop(3).PaddingBottom(1).Border(0.4f).BorderColor(Line).Background(Head)
            .PaddingVertical(1.5f).AlignCenter().Text(text).Bold().FontSize(9.5f);

    /// <summary>サブ区分の行見出し（全幅グレー帯。縦サイド見出しの代替）。</summary>
    private static void SubTitle(ColumnDescriptor col, string text)
        => col.Item().Border(0.4f).BorderColor(Line).Background(Head)
            .PaddingVertical(2).AlignCenter().Text(text).Bold().FontSize(8.5f);

    // ---- テーブルセル（汎用：従業員台帳）----
    private static void H(TableDescriptor t, string s, uint cs = 1, uint rs = 1, float fs = 8)
        => t.Cell().ColumnSpan(cs).RowSpan(rs).Border(0.4f).BorderColor(Line).Background(Head).PaddingVertical(7).PaddingHorizontal(3).AlignMiddle().AlignCenter().Text(s).FontSize(fs).Bold();

    private static void V(TableDescriptor t, string s, uint cs = 1, uint rs = 1, float fs = 9)
        => t.Cell().ColumnSpan(cs).RowSpan(rs).Border(0.4f).BorderColor(Line).PaddingVertical(7).PaddingHorizontal(3).AlignMiddle().Text(s).FontSize(fs);

    /// <summary>金額用＝右寄せ。</summary>
    private static void VR(TableDescriptor t, string s, uint cs = 1, uint rs = 1, float fs = 9)
        => t.Cell().ColumnSpan(cs).RowSpan(rs).Border(0.4f).BorderColor(Line).PaddingVertical(7).PaddingHorizontal(3).AlignMiddle().AlignRight().Text(s).FontSize(fs);

    /// <summary>人数・区分用＝中央寄せ。</summary>
    private static void VC(TableDescriptor t, string s, uint cs = 1, uint rs = 1, float fs = 9)
        => t.Cell().ColumnSpan(cs).RowSpan(rs).Border(0.4f).BorderColor(Line).PaddingVertical(7).PaddingHorizontal(3).AlignMiddle().AlignCenter().Text(s).FontSize(fs);

    // ---- コンパクトセル（導入設定台帳：密なフォーム用。行を詰める）----
    private static void Hc(TableDescriptor t, string s, uint cs = 1, uint rs = 1, float fs = 7)
        => t.Cell().ColumnSpan(cs).RowSpan(rs).Border(0.4f).BorderColor(Line).Background(Head).Padding(2).AlignMiddle().AlignCenter().Text(s).FontSize(fs).Bold();

    private static void Vc(TableDescriptor t, string s, uint cs = 1, float fs = 8)
        => t.Cell().ColumnSpan(cs).Border(0.4f).BorderColor(Line).PaddingVertical(2).PaddingHorizontal(3).MinHeight(11).AlignMiddle().Text(s).FontSize(fs);

    private static void VcC(TableDescriptor t, string s, uint cs = 1, float fs = 8)
        => t.Cell().ColumnSpan(cs).Border(0.4f).BorderColor(Line).PaddingVertical(2).PaddingHorizontal(3).MinHeight(11).AlignMiddle().AlignCenter().Text(s).FontSize(fs);
}
