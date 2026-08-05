using SharpAccess;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace SharpAccess.ProviderContractTests;

internal sealed class SqliteAuthorizationInvalidationTestContext : IAsyncDisposable
{
    public static DateTimeOffset Now { get; } = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath;

    public SqliteAuthorizationInvalidationTestContext()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dotnet-auth-provider-invalidation-{Guid.NewGuid():N}.db");
        SqliteAuthOptions options = new() { ConnectionString = $"Data Source={_databasePath};Pooling=False" };
        Store = new SqliteAuthStore(new SqliteAuthConnectionFactory(Options.Create(options)));
    }

    public SqliteAuthStore Store { get; }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return ValueTask.CompletedTask;
    }

    public async Task<AuthUser> CreateUserAsync()
    {
        await Store.InitializeAsync();
        Guid id = Guid.NewGuid();
        AuthUser user = new(
            id,
            $"person-{id:N}@example.com",
            $"PERSON-{id:N}@EXAMPLE.COM",
            "hash",
            EmailVerifiedUtc: Now,
            IsActive: true,
            FailedLoginAttempts: 0,
            LockoutEndUtc: null,
            SecurityVersion: 1,
            CreatedUtc: Now,
            UpdatedUtc: Now);

        bool created = await Store.CreateUserWithVerificationTokenAsync(
            user,
            $"verification-{Guid.NewGuid():N}",
            Now.AddHours(1));
        Assert.True(created);
        return user;
    }

    public async Task<RoleRecord> CreateRoleAsync(string suffix)
    {
        await Store.InitializeAsync();
        string name = $"Contract {suffix} {Guid.NewGuid():N}";
        RoleRecord? role = await Store.CreateRoleAsync(name, name.ToUpperInvariant(), "Provider contract role.", Now);
        Assert.NotNull(role);
        return role;
    }

    public async Task<PermissionRecord> FindPermissionAsync(string name)
    {
        await Store.InitializeAsync();
        return (await Store.ListPermissionsAsync(new AuthPageQuery(200, null))).Items.Single(permission => permission.Name == name);
    }

    public async Task<RefreshTokenRecord> CreateRefreshTokenAsync(AuthUser user, string prefix)
    {
        RefreshTokenRecord record = new(
            Guid.NewGuid(),
            user.Id,
            $"{prefix}-{Guid.NewGuid():N}",
            Guid.NewGuid(),
            user.SecurityVersion,
            "127.0.0.1",
            "provider-contract-test",
            Now,
            Now,
            Now.AddDays(30),
            RevokedUtc: null,
            ReplacedByTokenId: null);
        await Store.CreateRefreshTokenAsync(record);
        return record;
    }

    public async Task<AuthUser> RequireUserAsync(Guid userId)
    {
        AuthUser? user = await Store.FindUserByIdAsync(userId);
        Assert.NotNull(user);
        return user;
    }

    public async Task AssertRevokedAsync(string tokenHash, DateTimeOffset expectedRevokedUtc)
    {
        RefreshTokenRecord? record = await Store.FindRefreshTokenByHashAsync(tokenHash);
        Assert.NotNull(record);
        Assert.Equal(expectedRevokedUtc, record.RevokedUtc);
    }
}
