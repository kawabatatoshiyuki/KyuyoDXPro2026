using PayCalcDXPro.Data.Payroll;
using PayCalcDXPro.Data.Rate;
using PayCalcDXPro.Domain.Reference;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// <see cref="PayrollProcessor"/> の計算式を合成データ（手組みの保険料額表・料率）で網羅検証する。
/// 期待値は現行 F_給与処理 のロジックから手計算した既知値で、DB の状態に依存しない。
/// </summary>
public class PayrollProcessorTests
{
    // 50銭処理を必ず通すため、健保・介護に小数を持つ1区分の額表を用意する。
    private static RateBundle Bundle() => new()
    {
        料率 = new RateSettings { 雇用保険一般 = 6.0, 雇用保険一般以外 = 7.0 },
        額表 = new List<RateTableRow>
        {
            new()
            {
                No = 1, 報酬月額自 = 0, 報酬月額至 = null,
                健康保険 = 2873.9, 介護保険 = 500.7, 厚生年金 = 8052, 厚生年金基金 = 100,
            },
        },
    };

    private static PayrollProcessor Processor() => new(Bundle(), SampleData.TaxTables);

    private static PayrollEmployeeSettings 標準設定() => new()
    {
        健康保険加入 = true, 介護保険加入 = true, 厚生年金加入 = true, 年金基金加入 = true, 雇用保険加入 = true,
        報酬月額 = 200_000, 源泉区分 = 源泉区分.甲, 源泉扶養親族数 = 0, 労働保険適用 = "1", 端数処理 = 2,
    };

    [Fact]
    public void 社会保険_50銭処理チェーンが正しい()
    {
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 200_000 };
        Processor().Apply(e, 標準設定());

        Assert.Equal(200_000, e.総支給額);
        // 健保＝round50(2873.9)＝2874（小数 .9 ＞ .5 で切上）
        Assert.Equal(2_874, e.健康保険);
        // 介護＝round50(2873.9+500.7=3374.6)=3375 − 健保2874 ＝ 501
        Assert.Equal(501, e.介護保険);
        Assert.Equal(8_052, e.厚生年金);
        Assert.Equal(100, e.年金基金);
        // 雇用＝round50(200000 × 6.0 / 1000 = 1200) ＝ 1200
        Assert.Equal(1_200, e.雇用保険);
        Assert.Equal(2_874 + 501 + 8_052 + 100 + 1_200, e.社会保険合計); // 12727
        // 子育支援は未加入なので算出されず社保合計にも乗らない。
        Assert.Null(e.子育支援);
    }

    [Fact]
    public void 子育支援_額表値を直読みし社保合計とデータ個数に算入する()
    {
        // #161対応：給与の子育支援＝保険料額表の子育支援列を整数で直読み（50銭処理なし）。
        var bundle = new RateBundle
        {
            料率 = new RateSettings { 雇用保険一般 = 6.0, 雇用保険一般以外 = 7.0 },
            額表 = new List<RateTableRow>
            {
                new()
                {
                    No = 1, 報酬月額自 = 0, 報酬月額至 = null,
                    健康保険 = 2873.9, 介護保険 = 500.7, 厚生年金 = 8052, 厚生年金基金 = 100, 子育支援 = 350,
                },
            },
        };
        var proc = new PayrollProcessor(bundle, SampleData.TaxTables);
        var emp = 標準設定();
        emp.子育支援加入 = true;
        // 小規模共済は手入力（現行 txt_小規模共済）。算入されるか併せて検証する。
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 200_000, 小規模共済 = 1_000 };
        proc.Apply(e, emp);

        Assert.Equal(350, e.子育支援); // 額表値の直読み
        // 社会保険合計＝健保2874＋介護501＋子育支援350＋厚年8052＋年金基金100＋雇用1200＋小規模共済1000
        Assert.Equal(2_874 + 501 + 350 + 8_052 + 100 + 1_200 + 1_000, e.社会保険合計);
        // データ個数＝既定12 ＋ 子育支援 ＋ 小規模共済 ＝ 14
        Assert.Equal(14, e.データ個数);
    }

    [Fact]
    public void 雇用保険_労働保険一般以外の料率を使う()
    {
        var emp = 標準設定();
        emp.労働保険適用 = "2"; // 一般以外＝7.0
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 200_000 };
        Processor().Apply(e, emp);

        Assert.Equal(1_400, e.雇用保険); // round50(200000 × 7.0 / 1000 = 1400)
    }

    [Fact]
    public void 報酬月額ゼロなら保険料額表分はゼロ_雇用のみ算出()
    {
        var emp = 標準設定();
        emp.報酬月額 = 0;
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 200_000 };
        Processor().Apply(e, emp);

        Assert.Equal(0, e.健康保険);
        Assert.Equal(0, e.介護保険);
        Assert.Equal(0, e.厚生年金);
        Assert.Equal(0, e.年金基金);
        Assert.Equal(1_200, e.雇用保険); // 雇用は総支給ベースなので算出される
    }

    [Fact]
    public void 未加入の保険は算出しない()
    {
        var emp = 標準設定();
        emp.介護保険加入 = false;
        emp.年金基金加入 = false;
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 200_000 };
        Processor().Apply(e, emp);

        Assert.Equal(2_874, e.健康保険);
        Assert.Null(e.介護保険);
        Assert.Null(e.年金基金);
    }

    [Fact]
    public void 非課税手当は課税対象から除き非課税金額計に計上する()
    {
        var emp = 標準設定();
        emp.非課税手当[0] = true;  // 給与支給手当1＝非課税
        emp.非課税手当[1] = false; // 給与支給手当2＝課税
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 200_000 };
        e.給与支給手当[0] = 10_000;
        e.給与支給手当[1] = 5_000;
        e.非課税通勤費 = 8_000;
        Processor().Apply(e, emp);

        Assert.Equal(200_000 + 10_000 + 5_000 + 8_000, e.総支給額); // 223000
        Assert.Equal(10_000, e.非課税金額計);
        // 課税対象＝総支給 −(非課税通勤費8000 ＋ 非課税手当10000)− 社会保険合計
        int 社保 = e.社会保険合計 ?? 0;
        Assert.Equal(223_000 - 18_000 - 社保, e.課税対象額);
    }

    [Fact]
    public void 総控除と差引_控除項目と住民税還付徴収を含む()
    {
        var emp = 標準設定();
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 300_000, 住民税 = 12_000 };
        e.控除項目[0] = 3_000;  // 控除1
        e.控除項目[8] = 9_999;  // 控除9（総控除には含めない）
        e.還付金額 = 1_000;
        e.徴収金額 = 500;
        Processor().Apply(e, emp);

        int 社保 = e.社会保険合計 ?? 0;
        Assert.Equal(社保 + (e.所得税 ?? 0) + 12_000 + 3_000, e.総控除額); // 控除9は除外
        Assert.Equal((e.総支給額 ?? 0) - (e.総控除額 ?? 0) + 1_000 - 500, e.差引支給額);
    }

    [Fact]
    public void 計算しない区分は所得税を変更しない()
    {
        var emp = 標準設定();
        emp.源泉区分 = 源泉区分.計算しない;
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 300_000, 所得税 = 7_777 };
        Processor().Apply(e, emp);

        Assert.Equal(7_777, e.所得税);
    }

    [Theory]
    [InlineData(2, 166)] // 切捨
    [InlineData(1, 167)] // 四捨五入
    [InlineData(3, 167)] // 切上
    public void 時間外手当_時分換算と端数処理(int 端数, int 期待)
    {
        var emp = 標準設定();
        emp.端数処理 = 端数;
        emp.法定内残業単価 = 1_000;
        var e = new PayrollEntry { Number = "1", 支給回 = 1 };
        e.法定内残業 = 0.10; // 0時間10分 → 10/60 = 0.1667時間 → ×1000 ＝ 166.67
        int 時間外 = Processor().ComputeOvertimeAllowance(e, emp);
        Assert.Equal(期待, 時間外);
    }

    [Fact]
    public void 時間外手当_複数区分の合算()
    {
        var emp = 標準設定();
        emp.端数処理 = 2;
        emp.法定内残業単価 = 1_000; // 1.30 ＝ 1.5h → 1500
        emp.普通残業単価 = 1_200;   // 2.00 ＝ 2h   → 2400
        var e = new PayrollEntry { Number = "1", 支給回 = 1 };
        e.法定内残業 = 1.30;
        e.普通残業60h以下 = 2.00;
        Assert.Equal(1_500 + 2_400, Processor().ComputeOvertimeAllowance(e, emp));
    }

    [Fact]
    public void 日給者の基本給_出勤日数かける日給単価を端数処理()
    {
        var emp = 標準設定();
        emp.端数処理 = 2;
        emp.日給単価 = 10_000;
        Assert.Equal(215_000, Processor().ComputeDailyBasePay(21.5, emp));
    }

    [Fact]
    public void データ個数_ゼロより大きい項目数を数える()
    {
        var e = new PayrollEntry { Number = "1", 支給回 = 1, 基本給 = 200_000 };
        Processor().Apply(e, 標準設定());
        // 基本給/総支給額/健保/介護/厚年/年金基金/雇用/社会保険合計/課税対象額/所得税/総控除額/差引支給額 ＝ 12項目
        Assert.Equal(12, e.データ個数);
    }
}
