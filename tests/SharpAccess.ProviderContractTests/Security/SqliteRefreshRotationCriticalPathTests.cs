using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SharpAccess;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Sqlite;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
public sealed class SqliteRefreshRotationCriticalPathTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sharpaccess-refresh-critical-{Guid.NewGuid():N}.db");

    // Leaves database creation to each test so migration state remains explicit.
    public Task InitializeAsync() => Task.CompletedTask;

    // Removes every temporary SQLite artifact created by the current test instance.
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
        return Task.CompletedTask;
    }

    // Rejects an unknown token without inserting the caller-provided replacement.
    [Fact]
    public async Task MissingRefreshTokenRotationReturnsNotFoundWithoutPersistingReplacement()
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        RefreshTokenRecord replacement = Token(
            "A1B2C3D4" + new string('2', 56),
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            now);

        TokenRotationResult result = await store.RotateRefreshTokenAsync(
            "A1B2C3D4" + new string('1', 56),
            replacement,
            now,
            20);

        Assert.Equal(TokenRotationStatus.NotFound, result.Status);
        Assert.Null(result.UserId);
        Assert.Null(result.FamilyId);
        Assert.Null(await store.FindRefreshTokenByHashAsync(replacement.TokenHash));
    }

    // Rejects every caller-controlled ownership mismatch and revokes the persisted family.
    [Theory]
    [InlineData("user")]
    [InlineData("family")]
    [InlineData("security-version")]
    public async Task MismatchedReplacementRevokesFamilyAndReturnsUserInvalid(string mismatch)
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await InsertVerifiedUserAsync();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        Guid userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid familyId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        RefreshTokenRecord existing = Token("A1B2C3D4" + new string('3', 56), userId, familyId, now);
        RefreshTokenRecord sibling = Token("A1B2C3D4" + new string('4', 56), userId, familyId, now.AddSeconds(1));
        RefreshTokenRecord replacement = CreateMismatchedReplacement(mismatch, userId, familyId, now.AddSeconds(2));
        await store.CreateRefreshTokenAsync(existing);
        await store.CreateRefreshTokenAsync(sibling);

        TokenRotationResult result = await store.RotateRefreshTokenAsync(
            existing.TokenHash,
            replacement,
            now.AddMinutes(1),
            20);

        AssertInvalidFamilyOutcome(result, userId, familyId);
        Assert.Equal(now.AddMinutes(1), (await store.FindRefreshTokenByHashAsync(existing.TokenHash))!.RevokedUtc);
        Assert.Equal(now.AddMinutes(1), (await store.FindRefreshTokenByHashAsync(sibling.TokenHash))!.RevokedUtc);
        Assert.Null(await store.FindRefreshTokenByHashAsync(replacement.TokenHash));
    }

    // Rejects disabled users and persisted security-version drift before replacement insertion.
    [Theory]
    [InlineData("inactive")]
    [InlineData("security-version")]
    public async Task InvalidPersistedUserStateRevokesFamilyAndReturnsUserInvalid(string invalidState)
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await InsertVerifiedUserAsync();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        Guid userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid familyId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        RefreshTokenRecord existing = Token("A1B2C3D4" + new string('9', 56), userId, familyId, now);
        RefreshTokenRecord sibling = Token("A1B2C3D4" + new string('A', 56), userId, familyId, now.AddSeconds(1));
        RefreshTokenRecord replacement = Token("A1B2C3D4" + new string('B', 56), userId, familyId, now.AddSeconds(2));
        await store.CreateRefreshTokenAsync(existing);
        await store.CreateRefreshTokenAsync(sibling);
        await ApplyInvalidUserStateAsync(invalidState);

        TokenRotationResult result = await store.RotateRefreshTokenAsync(
            existing.TokenHash,
            replacement,
            now.AddMinutes(1),
            20);

        AssertInvalidFamilyOutcome(result, userId, familyId);
        Assert.Equal(now.AddMinutes(1), (await store.FindRefreshTokenByHashAsync(existing.TokenHash))!.RevokedUtc);
        Assert.Equal(now.AddMinutes(1), (await store.FindRefreshTokenByHashAsync(sibling.TokenHash))!.RevokedUtc);
        Assert.Null(await store.FindRefreshTokenByHashAsync(replacement.TokenHash));
    }

    // Revokes the family when persisted active tokens already exceed the configured cap.
    [Fact]
    public async Task ActiveTokenLimitExceededRevokesFamilyAndRejectsReplacement()
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await InsertVerifiedUserAsync();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        Guid userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid familyId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        RefreshTokenRecord existing = Token("A1B2C3D4" + new string('6', 56), userId, familyId, now);
        RefreshTokenRecord sibling = Token("A1B2C3D4" + new string('7', 56), userId, familyId, now.AddSeconds(1));
        RefreshTokenRecord replacement = Token("A1B2C3D4" + new string('8', 56), userId, familyId, now.AddSeconds(2));
        await store.CreateRefreshTokenAsync(existing);
        await store.CreateRefreshTokenAsync(sibling);

        TokenRotationResult result = await store.RotateRefreshTokenAsync(
            existing.TokenHash,
            replacement,
            now.AddMinutes(1),
            maximumActiveTokensPerFamily: 1);

        Assert.Equal(TokenRotationStatus.LimitExceeded, result.Status);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(familyId, result.FamilyId);
        Assert.Equal(now.AddMinutes(1), (await store.FindRefreshTokenByHashAsync(existing.TokenHash))!.RevokedUtc);
        Assert.Equal(now.AddMinutes(1), (await store.FindRefreshTokenByHashAsync(sibling.TokenHash))!.RevokedUtc);
        Assert.Null(await store.FindRefreshTokenByHashAsync(replacement.TokenHash));
    }

    // Creates the provider services used by each isolated contract test.
    private ServiceProvider BuildServices()
    {
        ServiceCollection services = new();
        services.AddSqliteAccess(options => options.ConnectionString = ConnectionString());
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Returns the isolated SQLite connection string for the current test instance.
    private string ConnectionString() => $"Data Source={_databasePath};Pooling=False;Foreign Keys=True";

    // Inserts the verified active user required by refresh-token foreign keys and validation.
    private Task InsertVerifiedUserAsync() =>
        ExecuteAsync(
            """
            INSERT INTO auth_users(
                id,email,normalized_email,password_hash,email_verified_utc,is_active,
                failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc)
            VALUES(
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','person@example.com','PERSON@EXAMPLE.COM','initial-hash',
                '2026-07-29T12:00:00.0000000+00:00',1,0,NULL,1,
                '2026-07-29T12:00:00.0000000+00:00','2026-07-29T12:00:00.0000000+00:00');
            """);

    // Applies one independently invalid persisted user condition.
    private Task ApplyInvalidUserStateAsync(string invalidState) =>
        ExecuteAsync(
            invalidState switch
            {
                "inactive" =>
                    "UPDATE auth_users SET is_active=0 WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';",
                "security-version" =>
                    "UPDATE auth_users SET security_version=2 WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';",
                _ => throw new ArgumentOutOfRangeException(nameof(invalidState))
            });

    // Executes one direct provider-fixture mutation against the isolated database.
    private async Task ExecuteAsync(string sql)
    {
        await using SqliteConnection connection = new(ConnectionString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    // Creates one mismatch while leaving every other replacement field valid.
    private static RefreshTokenRecord CreateMismatchedReplacement(
        string mismatch,
        Guid userId,
        Guid familyId,
        DateTimeOffset now) =>
        mismatch switch
        {
            "user" => Token(
                "A1B2C3D4" + new string('5', 56),
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                familyId,
                now),
            "family" => Token(
                "A1B2C3D4" + new string('5', 56),
                userId,
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                now),
            "security-version" => Token(
                "A1B2C3D4" + new string('5', 56),
                userId,
                familyId,
                now,
                securityVersion: 2),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };

    // Verifies the canonical user-invalid result returned for fail-closed family revocation.
    private static void AssertInvalidFamilyOutcome(
        TokenRotationResult result,
        Guid userId,
        Guid familyId)
    {
        Assert.Equal(TokenRotationStatus.UserInvalid, result.Status);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(familyId, result.FamilyId);
    }

    // Creates a canonical active refresh-token record for one user and family.
    private static RefreshTokenRecord Token(
        string hash,
        Guid userId,
        Guid familyId,
        DateTimeOffset now,
        int securityVersion = 1) =>
        new(
            Guid.NewGuid(),
            userId,
            hash,
            familyId,
            securityVersion,
            "127.0.0.1",
            "sqlite-critical-contract",
            now,
            now,
            now.AddDays(30),
            null,
            null);
}
