namespace PayCalcDXPro.Web;

/// <summary>
/// タブUIの「最後に選択したタブ」を画面横断で保持する。Blazor Server では Scoped＝回線（ブラウザセッション）の
/// 生存期間中保持されるため、リンクで別画面へ遷移して戻っても、また従業員を切り替えても、直前に開いていた
/// タブが復元される。キーはタブグループごとに一意（例："company" / "employee-editor" / "home-process"）。
/// </summary>
public sealed class TabStateService
{
    private readonly Dictionary<string, int> _active = new();

    /// <summary>指定タブグループの最後に選択したタブ番号（未記録なら0＝先頭）。</summary>
    public int Get(string key) => _active.TryGetValue(key, out var v) ? v : 0;

    /// <summary>指定タブグループの選択タブ番号を記録する。</summary>
    public void Set(string key, int value) => _active[key] = value < 0 ? 0 : value;
}
