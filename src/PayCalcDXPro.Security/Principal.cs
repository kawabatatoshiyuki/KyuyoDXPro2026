namespace PayCalcDXPro.Security;

/// <summary>ロール（権限）。マイナンバー復号は専用ロールに分離する。</summary>
public enum Role
{
    管理者,
    給与担当,
    マイナンバー閲覧,
}

/// <summary>認証済みの操作者（ID・氏名・ロール）。監査ログと認可の両方に使う。</summary>
public sealed record Principal(string Id, string Name, IReadOnlySet<Role> Roles)
{
    /// <summary>マイナンバーを全桁復号できるか（マイナンバー閲覧 または 管理者）。</summary>
    public bool CanDecryptMyNumber =>
        Roles.Contains(Role.マイナンバー閲覧) || Roles.Contains(Role.管理者);

    public bool IsInRole(Role role) => Roles.Contains(role);

    public static Principal Of(string id, string name, params Role[] roles)
        => new(id, name, new HashSet<Role>(roles));
}
