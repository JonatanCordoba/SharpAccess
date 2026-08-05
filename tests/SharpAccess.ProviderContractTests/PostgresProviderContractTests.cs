using SharpAccess.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Postgres")]
public sealed class PostgresProviderContractTests : AuthProviderContractTestBase
{
    private ServiceProvider? _provider;
    private IServiceScope? _scope;

    // Verifies that provider initialization is idempotent and seeds the authorization catalog once.
    [Trait("Capability", "MigrationContract")]
    [PostgresFact]
    public Task InitializationIsIdempotentAndSeedsAuthorizationCatalogOnce() =>
        InitializationIsIdempotentAndSeedsAuthorizationCatalogOnceCore();

    // Verifies that concurrent provider initialization remains safe and deterministic.
    [Trait("Capability", "MigrationContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentInitializationIsSafeAndDeterministic() =>
        ConcurrentInitializationIsSafeAndDeterministicCore();

    // Verifies the bounded pagination contract against PostgreSQL.
    [Trait("Capability", "PaginationContract")]
    [PostgresFact]
    public Task BoundedPaginationIsStableAndComplete() =>
        BoundedPaginationIsStableAndCompleteCore();

    // Verifies that a persisted refresh token can be retrieved by its keyed hash.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "TemporalRoundTripContract")]
    [PostgresFact]
    public Task RefreshTokenCanBeCreatedAndFoundByHash() =>
        RefreshTokenCanBeCreatedAndFoundByHashCore();

    // Verifies that rotation atomically revokes the old token and inserts the replacement.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "TransactionContract")]
    [PostgresFact]
    public Task RefreshTokenRotationRevokesExistingTokenAndInsertsReplacement() =>
        RefreshTokenRotationRevokesExistingTokenAndInsertsReplacementCore();

    // Verifies that replaying a revoked refresh token revokes the active token family.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [Trait("MutationInvariant", "PostgresRefreshReplay")]
    [PostgresFact]
    public Task ReusedRefreshTokenRevokesActiveFamilyMembers() =>
        ReusedRefreshTokenRevokesActiveFamilyMembersCore();

    // Verifies that expired refresh tokens are revoked and reported as expired instead of rotated.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [PostgresFact]
    public Task ExpiredRefreshTokenRotationReturnsExpiredAndDoesNotInsertReplacement() =>
        ExpiredRefreshTokenRotationReturnsExpiredAndDoesNotInsertReplacementCore();

    // Verifies that invalid persisted user state revokes the family and prevents replacement insertion.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [PostgresFact]
    public Task InvalidUserRefreshTokenRotationRevokesFamilyAndReturnsUserInvalid() =>
        InvalidUserRefreshTokenRotationRevokesFamilyAndReturnsUserInvalidCore();

    // Verifies that explicit family revocation only affects active tokens in that family.
    [Trait("Capability", "RefreshTokenContract")]
    [PostgresFact]
    public Task ExplicitRefreshTokenFamilyRevocationRevokesActiveFamilyTokens() =>
        ExplicitRefreshTokenFamilyRevocationRevokesActiveFamilyTokensCore();

    // Verifies that a general one-time token can be consumed exactly once.
    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "TransactionContract")]
    [PostgresFact]
    public Task GeneralOneTimeTokenCanBeConsumedOnlyOnce() =>
        GeneralOneTimeTokenCanBeConsumedOnlyOnceCore();

    // Verifies that expired general one-time tokens are not consumed.
    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [PostgresFact]
    public Task ExpiredGeneralOneTimeTokenIsNotConsumed() =>
        ExpiredGeneralOneTimeTokenIsNotConsumedCore();

    // Verifies that replacing an email verification token invalidates the previous active token.
    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "TransactionContract")]
    [PostgresFact]
    public Task ReplaceVerificationTokenConsumesPreviousActiveToken() =>
        ReplaceVerificationTokenConsumesPreviousActiveTokenCore();

    // Verifies that unsupported one-time token purposes fail explicitly instead of choosing an arbitrary table.
    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [PostgresFact]
    public Task UnsupportedOneTimeTokenPurposeFailsExplicitly() =>
        UnsupportedOneTimeTokenPurposeFailsExplicitlyCore();

    [Trait("Capability", "UserStoreContract")]
    [Trait("Capability", "TransactionContract")]
    [PostgresFact]
    public Task RegistrationRollsBackWhenVerificationTokenInsertFails() =>
        RegistrationRollsBackWhenVerificationTokenInsertFailsCore();

    [Trait("Capability", "UserStoreContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentRegistrationWithSameNormalizedEmailCreatesExactlyOne() =>
        ConcurrentRegistrationWithSameNormalizedEmailCreatesExactlyOneCore();

    [Trait("Capability", "PasswordStateContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentLoginFailuresReachLockoutThreshold() =>
        ConcurrentLoginFailuresReachLockoutThresholdCore();

    [Trait("Capability", "PasswordStateContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentPasswordResetsSucceedExactlyOnce() =>
        ConcurrentPasswordResetsSucceedExactlyOnceCore();

    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentEmailTokenReplacementLeavesOneActiveToken() =>
        ConcurrentEmailTokenReplacementLeavesOneActiveTokenCore();

    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentOneTimeTokenConsumptionSucceedsExactlyOnce() =>
        ConcurrentOneTimeTokenConsumptionSucceedsExactlyOnceCore();

    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentRefreshRotationDetectsReplay() =>
        ConcurrentRefreshRotationDetectsReplayCore();

    [Trait("Capability", "GlobalAuthorizationContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentGlobalRoleChangesRemainAtomic() =>
        ConcurrentGlobalRoleChangesRemainAtomicCore();

    [Trait("Capability", "TenantOwnershipContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [PostgresFact]
    public Task ConcurrentTenantOwnershipTransferElectsOneOwner() =>
        ConcurrentTenantOwnershipTransferElectsOneOwnerCore();

    // Creates the PostgreSQL auth store used by the inherited provider-contract tests.
    protected override async Task<object> CreateProviderStoreAsync()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString).ConfigureAwait(false);
        _provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        _scope = _provider.CreateScope();
        return _scope.ServiceProvider.GetRequiredService<IAuthStore>();
    }

    // Disposes provider services created for the current PostgreSQL contract test.
    protected override async Task DisposeProviderResourcesAsync()
    {
        _scope?.Dispose();
        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}

[Trait("Provider", "Postgres")]
[Trait("Capability", "MigrationContract")]
public sealed class PostgresProviderSmokeTests
{
    // Verifies PostgreSQL migrations are idempotent and recorded once.
    [PostgresFact]
    public async Task PostgresMigrationsAreRecordedOnce()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString).ConfigureAwait(false);
        await using ServiceProvider provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using IServiceScope scope = provider.CreateScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        IAuthMigrationProvider migrations = scope.ServiceProvider.GetRequiredService<IAuthMigrationProvider>();

        string[] expectedMigrationIds = migrations.GetMigrations()
            .Select(migration => migration.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await store.InitializeAsync().ConfigureAwait(false);
        await store.InitializeAsync().ConfigureAwait(false);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM auth_schema_migrations ORDER BY id;";

        List<string> recordedMigrationIds = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            recordedMigrationIds.Add(reader.GetString(0));
        }

        Assert.Equal(expectedMigrationIds, recordedMigrationIds);
    }
}

internal static class PostgresProviderContractTestSupport
{
    internal const string ConnectionStringEnvironmentVariable = "SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING";
    private static readonly string[] AuthTables =
    [
        "auth_migration_reconciliation_reports",
        "auth_schema_migration_checksums",
        "auth_schema_migrations",
        "auth_security_audit_logs",
        "auth_oauth_accounts",
        "auth_oauth_states",
        "auth_oauth_exchange_codes",
        "auth_password_reset_tokens",
        "auth_email_verification_tokens",
        "auth_one_time_tokens",
        "auth_refresh_tokens",
        "auth_tenant_owners",
        "auth_tenant_user_roles",
        "auth_tenant_role_permissions",
        "auth_tenant_permissions",
        "auth_tenant_roles",
        "auth_global_user_roles",
        "auth_global_role_permissions",
        "auth_global_permissions",
        "auth_global_roles",
        "auth_role_permissions",
        "auth_user_roles",
        "auth_tenant_memberships",
        "auth_tenants",
        "auth_permissions",
        "auth_roles",
        "auth_users"
    ];

    // Creates a service provider configured with the PostgreSQL provider package.
    internal static ServiceProvider CreateProvider(string connectionString)
    {
        ServiceCollection services = new();
        services.AddPostgresAccess(options =>
        {
            options.ConnectionString = connectionString;
        });
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Reads and validates the opt-in PostgreSQL scratch database connection string.
    internal static string RequireConnectionString() =>
        ProviderContractTestDatabaseGuard.RequireResettableConnectionString(
            "PostgreSQL",
            ConnectionStringEnvironmentVariable,
            connectionString => new NpgsqlConnectionStringBuilder(connectionString).Database);

    // Drops only the provider-owned auth tables in a scratch PostgreSQL database.
    internal static async Task ResetDatabaseAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        foreach (string table in AuthTables)
        {
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE IF EXISTS {table} CASCADE;";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class PostgresFactAttribute : FactAttribute
{
    // Skips opt-in PostgreSQL tests unless a scratch database connection string is provided.
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
            PostgresProviderContractTestSupport.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {PostgresProviderContractTestSupport.ConnectionStringEnvironmentVariable} to run PostgreSQL provider-contract tests.";
        }
    }
}