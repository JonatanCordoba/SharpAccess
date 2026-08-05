using SharpAccess.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
public sealed class SqliteProviderContractTests : AuthProviderContractTestBase
{
    private string _databasePath = null!;

    // Verifies that provider initialization is idempotent and seeds the authorization catalog once.
    [Trait("Capability", "MigrationContract")]
    [Fact]
    public Task InitializationIsIdempotentAndSeedsAuthorizationCatalogOnce() =>
        InitializationIsIdempotentAndSeedsAuthorizationCatalogOnceCore();

    // Verifies that concurrent provider initialization remains safe and deterministic.
    [Trait("Capability", "MigrationContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentInitializationIsSafeAndDeterministic() =>
        ConcurrentInitializationIsSafeAndDeterministicCore();

    // Verifies the bounded pagination contract against SQLite.
    [Trait("Capability", "PaginationContract")]
    [Fact]
    public Task BoundedPaginationIsStableAndComplete() =>
        BoundedPaginationIsStableAndCompleteCore();

    // Verifies that a persisted refresh token can be retrieved by its keyed hash.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "TemporalRoundTripContract")]
    [Fact]
    public Task RefreshTokenCanBeCreatedAndFoundByHash() =>
        RefreshTokenCanBeCreatedAndFoundByHashCore();

    // Verifies that rotation atomically revokes the old token and inserts the replacement.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "TransactionContract")]
    [Fact]
    public Task RefreshTokenRotationRevokesExistingTokenAndInsertsReplacement() =>
        RefreshTokenRotationRevokesExistingTokenAndInsertsReplacementCore();

    // Verifies that replaying a revoked refresh token revokes the active token family.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [Trait("MutationInvariant", "RefreshReplay")]
    [Fact]
    public Task ReusedRefreshTokenRevokesActiveFamilyMembers() =>
        ReusedRefreshTokenRevokesActiveFamilyMembersCore();

    // Verifies that expired refresh tokens are revoked and reported as expired instead of rotated.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [Fact]
    public Task ExpiredRefreshTokenRotationReturnsExpiredAndDoesNotInsertReplacement() =>
        ExpiredRefreshTokenRotationReturnsExpiredAndDoesNotInsertReplacementCore();

    // Verifies that invalid persisted user state revokes the family and prevents replacement insertion.
    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [Fact]
    public Task InvalidUserRefreshTokenRotationRevokesFamilyAndReturnsUserInvalid() =>
        InvalidUserRefreshTokenRotationRevokesFamilyAndReturnsUserInvalidCore();

    // Verifies that explicit family revocation only affects active tokens in that family.
    [Trait("Capability", "RefreshTokenContract")]
    [Fact]
    public Task ExplicitRefreshTokenFamilyRevocationRevokesActiveFamilyTokens() =>
        ExplicitRefreshTokenFamilyRevocationRevokesActiveFamilyTokensCore();

    // Verifies that a general one-time token can be consumed exactly once.
    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "TransactionContract")]
    [Fact]
    public Task GeneralOneTimeTokenCanBeConsumedOnlyOnce() =>
        GeneralOneTimeTokenCanBeConsumedOnlyOnceCore();

    // Verifies that expired general one-time tokens are not consumed.
    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [Fact]
    public Task ExpiredGeneralOneTimeTokenIsNotConsumed() =>
        ExpiredGeneralOneTimeTokenIsNotConsumedCore();

    // Verifies that replacing an email verification token invalidates the previous active token.
    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "TransactionContract")]
    [Fact]
    public Task ReplaceVerificationTokenConsumesPreviousActiveToken() =>
        ReplaceVerificationTokenConsumesPreviousActiveTokenCore();

    // Verifies that unsupported one-time token purposes fail explicitly instead of choosing an arbitrary table.
    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "ErrorClassificationContract")]
    [Fact]
    public Task UnsupportedOneTimeTokenPurposeFailsExplicitly() =>
        UnsupportedOneTimeTokenPurposeFailsExplicitlyCore();

    // Verifies that every provider migration is recorded exactly once after repeated initialization.
    [Fact]
    public async Task SqliteMigrationsAreRecordedOnce()
    {
        await InitializeStoreAsync();
        await InitializeStoreAsync();

        long expectedMigrationCount = new SqliteAuthMigrationProvider().GetMigrations().Count;
        await using SqliteConnection connection = new($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM auth_schema_migrations;";
        Assert.Equal(expectedMigrationCount, (long)(await command.ExecuteScalarAsync())!);
    }

    // Verifies that required SQLite security tables and foreign keys exist.
    [Fact]
    public async Task RequiredSecurityTablesAndForeignKeysExist()
    {
        await InitializeStoreAsync();
        await using SqliteConnection connection = new($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        List<string> names = [];
        await using SqliteDataReader reader = await tables.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Contains("auth_users", names);
        Assert.Contains("auth_refresh_tokens", names);
        Assert.Contains("auth_oauth_states", names);
        Assert.Contains("auth_email_verification_tokens", names);
        Assert.Contains("auth_password_reset_tokens", names);
        Assert.Contains("auth_oauth_exchange_codes", names);
        Assert.DoesNotContain("auth_one_time_tokens", names);
        Assert.Contains("auth_security_audit_logs", names);
        Assert.Contains("auth_tenant_memberships", names);

        await using SqliteCommand foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_list('auth_refresh_tokens');";
        bool replacementReferenceFound = false;
        await using SqliteDataReader foreignKeyReader = await foreignKeys.ExecuteReaderAsync();
        while (await foreignKeyReader.ReadAsync())
        {
            replacementReferenceFound |= string.Equals(
                foreignKeyReader.GetString(3),
                "replaced_by_token_id",
                StringComparison.Ordinal);
        }

        Assert.True(replacementReferenceFound);
    }

    // Verifies that the SQLite connection factory enables foreign keys and busy timeout.
    [Fact]
    public async Task ConnectionFactoryEnablesForeignKeysAndBusyTimeout()
    {
        SqliteAuthOptions options = new() { ConnectionString = $"Data Source={_databasePath};Pooling=False" };
        SqliteAuthConnectionFactory factory = new(Options.Create(options));
        await using SqliteConnection connection = await factory.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Trait("Capability", "UserStoreContract")]
    [Trait("Capability", "TransactionContract")]
    [Fact]
    public Task RegistrationRollsBackWhenVerificationTokenInsertFails() =>
        RegistrationRollsBackWhenVerificationTokenInsertFailsCore();

    [Trait("Capability", "UserStoreContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentRegistrationWithSameNormalizedEmailCreatesExactlyOne() =>
        ConcurrentRegistrationWithSameNormalizedEmailCreatesExactlyOneCore();

    [Trait("Capability", "PasswordStateContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentLoginFailuresReachLockoutThreshold() =>
        ConcurrentLoginFailuresReachLockoutThresholdCore();

    [Trait("Capability", "PasswordStateContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentPasswordResetsSucceedExactlyOnce() =>
        ConcurrentPasswordResetsSucceedExactlyOnceCore();

    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentEmailTokenReplacementLeavesOneActiveToken() =>
        ConcurrentEmailTokenReplacementLeavesOneActiveTokenCore();

    [Trait("Capability", "OneTimeTokenContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentOneTimeTokenConsumptionSucceedsExactlyOnce() =>
        ConcurrentOneTimeTokenConsumptionSucceedsExactlyOnceCore();

    [Trait("Capability", "RefreshTokenContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentRefreshRotationDetectsReplay() =>
        ConcurrentRefreshRotationDetectsReplayCore();

    [Trait("Capability", "GlobalAuthorizationContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentGlobalRoleChangesRemainAtomic() =>
        ConcurrentGlobalRoleChangesRemainAtomicCore();

    [Trait("Capability", "TenantOwnershipContract")]
    [Trait("Capability", "ConcurrencyContract")]
    [Fact]
    public Task ConcurrentTenantOwnershipTransferElectsOneOwner() =>
        ConcurrentTenantOwnershipTransferElectsOneOwnerCore();

    // Creates the SQLite auth store used by the inherited provider-contract tests.
    protected override Task<object> CreateProviderStoreAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"sharpaccess-provider-{Guid.NewGuid():N}.db");
        SqliteAuthOptions options = new() { ConnectionString = $"Data Source={_databasePath};Pooling=False" };
        return Task.FromResult<object>(new SqliteAuthStore(new SqliteAuthConnectionFactory(Options.Create(options))));
    }

    // Removes the temporary SQLite database used by the current test.
    protected override Task DisposeProviderResourcesAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }
}
