namespace PayCalcDXPro.Security;

/// <summary>利用者アカウント（現行 M_セキュリティアカウント設定 にロールを追加した形）。</summary>
public sealed record UserAccount
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>PBKDF2 ハッシュ（<see cref="PasswordHasher"/> 形式）。</summary>
    public required string PasswordHash { get; init; }
    public required IReadOnlySet<Role> Roles { get; init; }
}

/// <summary>アカウントの取得元（本番は SQLCipher の M_セキュリティアカウント設定 を実装する）。</summary>
public interface IUserStore
{
    UserAccount? FindById(string id);
}

/// <summary>認証（ログイン）。ID/パスワードを検証して <see cref="Principal"/> を返す。</summary>
public sealed class AuthenticationService
{
    private readonly IUserStore _store;

    public AuthenticationService(IUserStore store) => _store = store;

    /// <summary>認証成功なら Principal、失敗なら null。</summary>
    public Principal? Authenticate(string id, string password)
    {
        UserAccount? user = _store.FindById(id);
        if (user is null) return null;
        if (!PasswordHasher.Verify(password, user.PasswordHash)) return null;
        return new Principal(user.Id, user.Name, user.Roles);
    }
}
