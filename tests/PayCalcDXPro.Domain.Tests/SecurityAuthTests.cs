using System.Security.Cryptography;
using PayCalcDXPro.Security;
using Xunit;

namespace PayCalcDXPro.Domain.Tests;

/// <summary>認証・認可・鍵保護（Security層）の検証。</summary>
public class SecurityAuthTests
{
    [Fact]
    public void パスワードはハッシュ化され_正しいときのみ検証成功()
    {
        string hash = PasswordHasher.Hash("P@ssw0rd!");
        Assert.DoesNotContain("P@ssw0rd!", hash);
        Assert.StartsWith("pbkdf2$", hash);
        Assert.True(PasswordHasher.Verify("P@ssw0rd!", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

    [Fact]
    public void 同じパスワードでもソルトで毎回異なるハッシュになる()
    {
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    [Fact]
    public void 認証は正しい資格情報でPrincipalを返す()
    {
        var store = new FakeUserStore(new UserAccount
        {
            Id = "pay01",
            Name = "給与花子",
            PasswordHash = PasswordHasher.Hash("secret"),
            Roles = new HashSet<Role> { Role.給与担当 },
        });
        var auth = new AuthenticationService(store);

        Principal? ok = auth.Authenticate("pay01", "secret");
        Assert.NotNull(ok);
        Assert.Equal("給与花子", ok!.Name);
        Assert.True(ok.IsInRole(Role.給与担当));

        Assert.Null(auth.Authenticate("pay01", "bad"));     // パスワード誤り
        Assert.Null(auth.Authenticate("unknown", "secret")); // 不明ID
    }

    [Theory]
    [InlineData(Role.管理者, true)]
    [InlineData(Role.マイナンバー閲覧, true)]
    [InlineData(Role.給与担当, false)]
    public void マイナンバー復号可否はロールで決まる(Role role, bool expected)
    {
        var p = Principal.Of("u", "n", role);
        Assert.Equal(expected, p.CanDecryptMyNumber);
    }

    [Fact]
    public void DPAPIで鍵を保護保存し復元できる()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows 専用

        string path = Path.Combine(Path.GetTempPath(), $"paycalc_key_{Guid.NewGuid():N}.bin");
        try
        {
            byte[] key = RandomNumberGenerator.GetBytes(32);
            DpapiKeyProtector.ProtectToFile(key, path, System.Security.Cryptography.DataProtectionScope.CurrentUser);

            // 保存ファイルは平文鍵そのものではない
            byte[] onDisk = File.ReadAllBytes(path);
            Assert.NotEqual(key, onDisk);

            byte[] restored = DpapiKeyProtector.UnprotectFromFile(path, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            Assert.Equal(key, restored);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreateは初回生成し2回目は同じ鍵を返す()
    {
        if (!OperatingSystem.IsWindows()) return;

        string path = Path.Combine(Path.GetTempPath(), $"paycalc_key_{Guid.NewGuid():N}.bin");
        try
        {
            byte[] first = DpapiKeyProtector.LoadOrCreate(path, 32, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            byte[] second = DpapiKeyProtector.LoadOrCreate(path, 32, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            Assert.Equal(first, second);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FakeUserStore : IUserStore
    {
        private readonly UserAccount _user;
        public FakeUserStore(UserAccount user) => _user = user;
        public UserAccount? FindById(string id) => id == _user.Id ? _user : null;
    }
}
