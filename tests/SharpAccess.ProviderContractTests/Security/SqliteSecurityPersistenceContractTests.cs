
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Sqlite;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
public sealed class SqliteSecurityPersistenceContractTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sharpaccess-security-contract-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MigrationPersistsHashKeyVersionsAcrossAllOpaqueTokenTables()
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        SharpAccessSchemaStatus status = await services.GetSharpAccessSchemaStatusAsync();

        Assert.True(status.IsCurrent);
        Assert.Equal(12, status.AppliedMigrations.Count);
        await using SqliteConnection connection = new(ConnectionString());
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('auth_refresh_tokens') WHERE name='hash_key_version';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('auth_refresh_tokens') WHERE name='authenticated_utc' AND [notnull]=1;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('auth_email_verification_tokens') WHERE name='hash_key_version';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('auth_password_reset_tokens') WHERE name='hash_key_version';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('auth_oauth_exchange_codes') WHERE name='hash_key_version';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('auth_oauth_states') WHERE name='hash_key_version';"));
    }

    [Fact]
    public async Task SqliteEnforcesActiveRefreshFamilyAndTokenCapsTransactionally()
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await using (SqliteConnection connection = new(ConnectionString()))
        {
            await connection.OpenAsync();
            await ExecuteAsync(
                connection,
                """
                INSERT INTO auth_users(
                    id,email,normalized_email,password_hash,email_verified_utc,is_active,
                    failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc)
                VALUES(
                    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','person@example.com','PERSON@EXAMPLE.COM',NULL,
                    '2026-07-14T12:00:00.0000000+00:00',1,0,NULL,1,
                    '2026-07-14T12:00:00.0000000+00:00','2026-07-14T12:00:00.0000000+00:00');
                """);
        }

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Guid userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid family = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        RefreshTokenRecord first = Token("A1B2C3D4" + new string('1', 56), userId, family, now);
        RefreshTokenRecord second = Token("A1B2C3D4" + new string('2', 56), userId, family, now.AddSeconds(1));
        RefreshTokenRecord otherFamily = Token(
            "A1B2C3D4" + new string('3', 56),
            userId,
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            now.AddSeconds(2));

        Assert.True(await store.TryCreateRefreshTokenAsync(first, 1, 1));
        Assert.False(await store.TryCreateRefreshTokenAsync(second, 1, 1));
        Assert.False(await store.TryCreateRefreshTokenAsync(otherFamily, 1, 1));

        await using SqliteConnection verification = new(ConnectionString());
        await verification.OpenAsync();
        Assert.Equal(
            "A1B2C3D4",
            Convert.ToString(
                await ScalarObjectAsync(verification, "SELECT hash_key_version FROM auth_refresh_tokens LIMIT 1;"),
                CultureInfo.InvariantCulture));
    }

    // Proves an audit constraint failure rolls back password and session state before a fresh retry succeeds.
    [Fact]
    public async Task PasswordChangeAuditFailureRollsBackMutationAndFreshRetrySucceeds()
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await InsertVerifiedUserAsync();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Guid userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        RefreshTokenRecord session = Token(
            "A1B2C3D4" + new string('4', 56),
            userId,
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            now);
        await store.CreateRefreshTokenAsync(session);

        Guid duplicateId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        await store.WriteAuditAsync(SecurityAuditEvidence.Create(
            duplicateId,
            now,
            "audit_seed",
            userId,
            null,
            null,
            null,
            null));
        AuditRecord duplicate = SecurityAuditEvidence.Create(
            duplicateId,
            now.AddMinutes(1),
            "password_changed",
            userId,
            null,
            "127.0.0.1",
            "provider-contract",
            null);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.ChangePasswordAsync(userId, "replacement-hash", now.AddMinutes(1), duplicate));

        await using (SqliteConnection verification = new(ConnectionString()))
        {
            await verification.OpenAsync();
            Assert.Equal("initial-hash", Convert.ToString(await ScalarObjectAsync(verification, "SELECT password_hash FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"), CultureInfo.InvariantCulture));
            Assert.Equal(1L, await ScalarAsync(verification, "SELECT security_version FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"));
            Assert.Equal(0L, await ScalarAsync(verification, "SELECT COUNT(*) FROM auth_refresh_tokens WHERE id='" + session.Id.ToString("D", CultureInfo.InvariantCulture) + "' AND revoked_utc IS NOT NULL;"));
        }

        AuditRecord retry = duplicate with { Id = Guid.NewGuid(), CreatedUtc = now.AddMinutes(2) };
        Assert.True(await store.ChangePasswordAsync(userId, "replacement-hash", now.AddMinutes(2), retry));
        Assert.Equal(now.AddMinutes(2), (await store.FindRefreshTokenByHashAsync(session.TokenHash))!.RevokedUtc);
        await using SqliteConnection succeeded = new(ConnectionString());
        await succeeded.OpenAsync();
        Assert.Equal("replacement-hash", Convert.ToString(await ScalarObjectAsync(succeeded, "SELECT password_hash FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"), CultureInfo.InvariantCulture));
        Assert.Equal(2L, await ScalarAsync(succeeded, "SELECT security_version FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"));
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_security_audit_logs WHERE id='" + retry.Id.ToString("D", CultureInfo.InvariantCulture) + "';"));
    }

    // Proves administrator reseeding, role assignment, and session revocation roll back with a failed audit insert.
    [Fact]
    public async Task AdministratorReseedAuditFailureRollsBackAllMutationsAndFreshRetrySucceeds()
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await InsertVerifiedUserAsync();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Guid userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        RefreshTokenRecord session = Token(
            "A1B2C3D4" + new string('8', 56),
            userId,
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            now);
        await store.CreateRefreshTokenAsync(session);

        Guid duplicateId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        await store.WriteAuditAsync(SecurityAuditEvidence.Create(duplicateId, now, "audit_seed", userId, null, null, null, null));
        AuditRecord duplicate = SecurityAuditEvidence.Create(
            duplicateId,
            now.AddMinutes(1),
            "administrator_seeded",
            userId,
            null,
            null,
            "provider-contract",
            null);
        AdminSeedOptions options = new() { Email = "person@example.com", Password = "unused-by-store" };

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.SeedAdminAsync(options, "seeded-hash", now.AddMinutes(1), duplicate));

        await using (SqliteConnection verification = new(ConnectionString()))
        {
            await verification.OpenAsync();
            Assert.Equal("initial-hash", Convert.ToString(await ScalarObjectAsync(verification, "SELECT password_hash FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"), CultureInfo.InvariantCulture));
            Assert.Equal(1L, await ScalarAsync(verification, "SELECT security_version FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"));
            Assert.Equal(0L, await ScalarAsync(verification, "SELECT COUNT(*) FROM auth_refresh_tokens WHERE id='" + session.Id.ToString("D", CultureInfo.InvariantCulture) + "' AND revoked_utc IS NOT NULL;"));
            Assert.Equal(0L, await ScalarAsync(verification, "SELECT COUNT(*) FROM auth_global_user_roles WHERE user_id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' AND role_id='10000000-0000-0000-0000-000000000001';"));
        }

        AuditRecord retry = duplicate with { Id = Guid.NewGuid(), CreatedUtc = now.AddMinutes(2) };
        await store.SeedAdminAsync(options, "seeded-hash", now.AddMinutes(2), retry);

        await using SqliteConnection succeeded = new(ConnectionString());
        await succeeded.OpenAsync();
        Assert.Equal("seeded-hash", Convert.ToString(await ScalarObjectAsync(succeeded, "SELECT password_hash FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"), CultureInfo.InvariantCulture));
        Assert.Equal(2L, await ScalarAsync(succeeded, "SELECT security_version FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"));
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_refresh_tokens WHERE id='" + session.Id.ToString("D", CultureInfo.InvariantCulture) + "' AND revoked_utc IS NOT NULL;"));
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_global_user_roles WHERE user_id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' AND role_id='10000000-0000-0000-0000-000000000001';"));
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_security_audit_logs WHERE id='" + retry.Id.ToString("D", CultureInfo.InvariantCulture) + "';"));
    }

    // Proves a new external binding, local user, and baseline role roll back together when binding evidence fails.
    [Fact]
    public async Task OAuthAccountBindingAuditFailureRollsBackAllMutationsAndExistingBindingDoesNotDuplicateEvidence()
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Guid duplicateId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        await store.WriteAuditAsync(SecurityAuditEvidence.Create(duplicateId, now, "audit_seed", null, null, null, null, null));
        AuditRecord duplicate = SecurityAuditEvidence.Create(
            duplicateId,
            now.AddMinutes(1),
            "oauth_account_linked",
            null,
            null,
            "127.0.0.1",
            "provider-contract",
            "provider=oidc");

        await Assert.ThrowsAsync<SqliteException>(() => store.ResolveOAuthUserAsync(
            "oidc",
            "external-subject",
            "external@example.com",
            "EXTERNAL@EXAMPLE.COM",
            now.AddMinutes(1),
            duplicate));

        await using (SqliteConnection verification = new(ConnectionString()))
        {
            await verification.OpenAsync();
            Assert.Equal(0L, await ScalarAsync(verification, "SELECT COUNT(*) FROM auth_users WHERE normalized_email='EXTERNAL@EXAMPLE.COM';"));
            Assert.Equal(0L, await ScalarAsync(verification, "SELECT COUNT(*) FROM auth_oauth_accounts WHERE provider='oidc' AND provider_subject='external-subject';"));
            Assert.Equal(0L, await ScalarAsync(verification, "SELECT COUNT(*) FROM auth_global_user_roles;"));
        }

        AuditRecord retry = duplicate with { Id = Guid.NewGuid(), CreatedUtc = now.AddMinutes(2) };
        ServiceResult<AuthUser> linked = await store.ResolveOAuthUserAsync(
            "oidc",
            "external-subject",
            "external@example.com",
            "EXTERNAL@EXAMPLE.COM",
            now.AddMinutes(2),
            retry);
        Assert.True(linked.Succeeded);
        Assert.NotNull(linked.Value);

        ServiceResult<AuthUser> existing = await store.ResolveOAuthUserAsync(
            "oidc",
            "external-subject",
            "external@example.com",
            "EXTERNAL@EXAMPLE.COM",
            now.AddMinutes(3),
            retry);
        Assert.True(existing.Succeeded);
        Assert.Equal(linked.Value.Id, existing.Value!.Id);

        string userId = linked.Value.Id.ToString("D", CultureInfo.InvariantCulture);
        await using SqliteConnection succeeded = new(ConnectionString());
        await succeeded.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_users WHERE id='" + userId + "';"));
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_oauth_accounts WHERE user_id='" + userId + "' AND provider='oidc' AND provider_subject='external-subject';"));
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_global_user_roles WHERE user_id='" + userId + "' AND role_id='10000000-0000-0000-0000-000000000002';"));
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_security_audit_logs WHERE id='" + retry.Id.ToString("D", CultureInfo.InvariantCulture) + "' AND user_id='" + userId + "';"));
    }

    // Proves replay-family revocation is rolled back when its canonical audit insert fails.
    [Fact]
    public async Task RefreshReplayHandlerAuditFailureLeavesFamilyActiveUntilFreshRetry()
    {
        await using ServiceProvider services = BuildServices();
        await services.MigrateSharpAccessAsync();
        await InsertVerifiedUserAsync();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        DateTimeOffset now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        Guid userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid familyId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        RefreshTokenRecord existing = Token("A1B2C3D4" + new string('5', 56), userId, familyId, now);
        RefreshTokenRecord activeReplacement = Token("A1B2C3D4" + new string('6', 56), userId, familyId, now.AddSeconds(1));
        await store.CreateRefreshTokenAsync(existing);
        Assert.Equal(
            TokenRotationStatus.Success,
            (await store.RotateRefreshTokenAsync(existing.TokenHash, activeReplacement, now.AddMinutes(1), 20)).Status);

        Guid duplicateId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        await store.WriteAuditAsync(SecurityAuditEvidence.Create(duplicateId, now, "audit_seed", userId, null, null, null, null));
        AuditRecord failing = SecurityAuditEvidence.Create(
            duplicateId,
            now.AddMinutes(2),
            "refresh_token_reuse_detected",
            userId,
            null,
            null,
            null,
            $"family={familyId:D}");

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.HandleRefreshTokenReplayAsync(existing.TokenHash, now.AddMinutes(2), failing));
        Assert.Null((await store.FindRefreshTokenByHashAsync(activeReplacement.TokenHash))!.RevokedUtc);

        AuditRecord retry = SecurityAuditEvidence.Create(
            now.AddMinutes(3),
            "refresh_token_reuse_detected",
            userId,
            null,
            null,
            null,
            $"family={familyId:D}");
        Assert.True(await store.HandleRefreshTokenReplayAsync(
            existing.TokenHash,
            now.AddMinutes(3),
            retry));
        Assert.Equal(now.AddMinutes(3), (await store.FindRefreshTokenByHashAsync(activeReplacement.TokenHash))!.RevokedUtc);
        await using SqliteConnection succeeded = new(ConnectionString());
        await succeeded.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(succeeded, "SELECT COUNT(*) FROM auth_security_audit_logs WHERE id='" + retry.Id.ToString("D", CultureInfo.InvariantCulture) + "';"));
    }

    private ServiceProvider BuildServices()
    {
        ServiceCollection services = new();
        services.AddSqliteAccess(options => options.ConnectionString = ConnectionString());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private string ConnectionString() => $"Data Source={_databasePath};Pooling=False;Foreign Keys=True";

    // Inserts the common verified user used by atomic audit-evidence tests.
    private async Task InsertVerifiedUserAsync()
    {
        await using SqliteConnection connection = new(ConnectionString());
        await connection.OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO auth_users(
                id,email,normalized_email,password_hash,email_verified_utc,is_active,
                failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc)
            VALUES(
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','person@example.com','PERSON@EXAMPLE.COM','initial-hash',
                '2026-07-14T12:00:00.0000000+00:00',1,0,NULL,1,
                '2026-07-14T12:00:00.0000000+00:00','2026-07-14T12:00:00.0000000+00:00');
            """);
    }

    private static RefreshTokenRecord Token(
        string hash,
        Guid userId,
        Guid familyId,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            userId,
            hash,
            familyId,
            1,
            "127.0.0.1",
            "security-contract",
            now,
            now,
            now.AddDays(30),
            null,
            null);

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql) =>
        Convert.ToInt64(await ScalarObjectAsync(connection, sql), CultureInfo.InvariantCulture);

    private static async Task<object?> ScalarObjectAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
