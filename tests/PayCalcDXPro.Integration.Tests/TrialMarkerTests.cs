using PayCalcDXPro.Security;
using Xunit;

namespace PayCalcDXPro.Integration.Tests;

/// <summary>
/// 体験版マーカー <see cref="TrialMarker"/> と年度ロック <see cref="TrialGuard"/> の検証。
/// 暗号(AES-128-CBC/PKCS7・先頭IV)＋署名(SHA-256)の往復と、現行 taikenbanCheck と同じ分岐
/// （初回作成→許可／同一年度→許可／別年度→ブロック／改ざん→ブロック）を確認する。
/// 実機の %ProgramData% に触れないよう、ベースフォルダをテンポラリへ差し替える。
/// </summary>
public class TrialMarkerTests : IDisposable
{
    private readonly string _dir;

    public TrialMarkerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"PayCalcTrial_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        TrialMarker.BaseDirectoryOverride = _dir;
    }

    [Fact]
    public void 書込んだマーカーを正しく読み戻せる()
    {
        TrialMarker.Write(2026);
        var info = TrialMarker.Read();

        Assert.Equal(TrialMarker.MarkerStatus.Valid, info.Status);
        Assert.Equal(2026, info.FirstTrialYear);
    }

    [Fact]
    public void ファイルが無ければNotFound()
        => Assert.Equal(TrialMarker.MarkerStatus.NotFound, TrialMarker.Read().Status);

    [Fact]
    public void 改ざん_中身書換えはTamperedになる()
    {
        TrialMarker.Write(2026);
        // 暗号文を破壊（復号失敗 or 署名不一致）。
        File.WriteAllText(TrialMarker.FilePath, "broken-content-xxxx");
        Assert.Equal(TrialMarker.MarkerStatus.Tampered, TrialMarker.Read().Status);
    }

    [Fact]
    public void TrialGuard_初回は許可しマーカーを作成する()
    {
        var g = new TrialGuard(enabled: true, currentYear: 2026);
        Assert.False(g.Evaluation.Blocked);
        Assert.Equal(TrialMarker.MarkerStatus.Valid, TrialMarker.Read().Status);
    }

    [Fact]
    public void TrialGuard_同一年度は許可()
    {
        TrialMarker.Write(2026);
        var g = new TrialGuard(enabled: true, currentYear: 2026);
        Assert.False(g.Evaluation.Blocked);
    }

    [Fact]
    public void TrialGuard_別年度はブロック()
    {
        TrialMarker.Write(2025);
        var g = new TrialGuard(enabled: true, currentYear: 2026);
        Assert.True(g.Evaluation.Blocked);
        Assert.Equal(TrialBlockReason.DifferentYear, g.Evaluation.Reason);
        Assert.Contains("2025", g.Evaluation.Message);
    }

    [Fact]
    public void TrialGuard_無効化なら常に許可()
    {
        TrialMarker.Write(2025);   // 別年度の形跡があっても
        var g = new TrialGuard(enabled: false, currentYear: 2026);
        Assert.False(g.Evaluation.Blocked);
    }

    public void Dispose()
    {
        TrialMarker.BaseDirectoryOverride = null;
        try { Directory.Delete(_dir, recursive: true); } catch { /* 無視 */ }
    }
}
