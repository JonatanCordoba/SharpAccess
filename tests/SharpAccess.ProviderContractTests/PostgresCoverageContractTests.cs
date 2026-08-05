using System.Data;
using System.Data.Common;
using System.Globalization;
using SharpAccess;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Postgres")]
public sealed class PostgresCoverageContractTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantManagerRoleId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private ServiceProvider? _provider;
    private IServiceScope? _scope;
    private IAuthStore _store = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(_connectionString).ConfigureAwait(false);
        _provider = PostgresProviderContractTestSupport.CreateProvider(_connectionString);
        _scope = _provider.CreateScope();
        _store = _scope.ServiceProvider.GetRequiredService<IAuthStore>();
        await _store.InitializeAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Trait("Capability", "OAuthPersistenceContract")]
    [PostgresFact]
    public async Task OAuthStateAndAccountLifecycleCoversSuccessConflictAndInactivePaths()
    {
        OAuthStateRecord activeState = new(
            Guid.NewGuid(),
            "oidc",
            $"state-{Guid.NewGuid():N}",
            "protected-verifier",
            "/return",
            Now,
            Now.AddMinutes(10),
            ConsumedUtc: null);
        await _store.SaveOAuthStateAsync(activeState).ConfigureAwait(false);

        OAuthStateRecord? consumed = await _store.ConsumeOAuthStateAsync(
            activeState.Provider,
            activeState.StateHash,
            Now.AddMinutes(1)).ConfigureAwait(false);
        Assert.NotNull(consumed);
        Assert.Equal(activeState.Id, consumed.Id);
        Assert.Equal(Now.AddMinutes(1), consumed.ConsumedUtc);
        Assert.Null(await _store.ConsumeOAuthStateAsync(
            activeState.Provider,
            activeState.StateHash,
            Now.AddMinutes(2)).ConfigureAwait(false));

        OAuthStateRecord expiredState = activeState with
        {
            Id = Guid.NewGuid(),
            StateHash = $"expired-{Guid.NewGuid():N}",
            ExpiresUtc = Now.AddMinutes(-1)
        };
        await _store.SaveOAuthStateAsync(expiredState).ConfigureAwait(false);
        Assert.Null(await _store.ConsumeOAuthStateAsync(
            expiredState.Provider,
            expiredState.StateHash,
            Now).ConfigureAwait(false));

        ServiceResult<AuthUser> created = await _store.ResolveOAuthUserAsync(
            "oidc",
            "external-subject",
            "external@example.com",
            "EXTERNAL@EXAMPLE.COM",
            Now.AddMinutes(3)).ConfigureAwait(false);
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Value);
        Assert.Null(created.Value.PasswordHash);
        Assert.True(created.Value.EmailVerifiedUtc.HasValue);

        ServiceResult<AuthUser> existing = await _store.ResolveOAuthUserAsync(
            "oidc",
            "external-subject",
            "external@example.com",
            "EXTERNAL@EXAMPLE.COM",
            Now.AddMinutes(4)).ConfigureAwait(false);
        Assert.True(existing.Succeeded);
        Assert.Equal(created.Value.Id, existing.Value!.Id);

        Assert.True(await _store.SetUserActiveAsync(
            created.Value.Id,
            isActive: false,
            Now.AddMinutes(5)).ConfigureAwait(false));
        ServiceResult<AuthUser> inactive = await _store.ResolveOAuthUserAsync(
            "oidc",
            "external-subject",
            "external@example.com",
            "EXTERNAL@EXAMPLE.COM",
            Now.AddMinutes(6)).ConfigureAwait(false);
        Assert.False(inactive.Succeeded);
        Assert.Equal(AuthError.Unauthorized, inactive.Error);

        AuthUser conflictUser = await CreateUserAsync(emailVerified: false, email: "conflict@example.com").ConfigureAwait(false);
        ServiceResult<AuthUser> conflict = await _store.ResolveOAuthUserAsync(
            "oidc",
            "different-subject",
            conflictUser.Email,
            conflictUser.NormalizedEmail,
            Now.AddMinutes(7)).ConfigureAwait(false);
        Assert.False(conflict.Succeeded);
        Assert.Equal(AuthError.Conflict, conflict.Error);
    }

    [Trait("Capability", "UserStoreContract")]
    [PostgresFact]
    public async Task UserStateMutationsResetFailuresRotateCredentialsAndRevokeSessions()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true).ConfigureAwait(false);
        DateTimeOffset lockoutEnd = Now.AddMinutes(20);
        await _store.RecordLoginFailureAsync(user.Id, 2, lockoutEnd, Now.AddMinutes(1)).ConfigureAwait(false);
        await _store.RecordLoginFailureAsync(user.Id, 2, lockoutEnd, Now.AddMinutes(2)).ConfigureAwait(false);

        AuthUser locked = (await _store.FindUserByIdAsync(user.Id).ConfigureAwait(false))!;
        Assert.Equal(2, locked.FailedLoginAttempts);
        Assert.Equal(lockoutEnd, locked.LockoutEndUtc);

        await _store.ResetLoginFailuresAsync(user.Id, Now.AddMinutes(3)).ConfigureAwait(false);
        AuthUser reset = (await _store.FindUserByIdAsync(user.Id).ConfigureAwait(false))!;
        Assert.Equal(0, reset.FailedLoginAttempts);
        Assert.Null(reset.LockoutEndUtc);

        Assert.False(await _store.UpdatePasswordHashAsync(
            user.Id,
            "wrong-hash",
            user.SecurityVersion,
            "ignored-hash",
            Now.AddMinutes(4)).ConfigureAwait(false));
        Assert.True(await _store.UpdatePasswordHashAsync(
            user.Id,
            "hash",
            user.SecurityVersion,
            "upgraded-hash",
            Now.AddMinutes(4)).ConfigureAwait(false));

        RefreshTokenRecord session = CreateRefreshToken(user, Guid.NewGuid(), "user-session", Now.AddMinutes(4));
        await _store.CreateRefreshTokenAsync(session).ConfigureAwait(false);
        Assert.True(await _store.ChangePasswordAsync(
            user.Id,
            "changed-hash",
            Now.AddMinutes(5)).ConfigureAwait(false));

        AuthUser changed = (await _store.FindUserByIdAsync(user.Id).ConfigureAwait(false))!;
        Assert.Equal("changed-hash", changed.PasswordHash);
        Assert.Equal(2, changed.SecurityVersion);
        Assert.Equal(Now.AddMinutes(5), (await _store.FindRefreshTokenByHashAsync(session.TokenHash).ConfigureAwait(false))!.RevokedUtc);

        Assert.True(await _store.SetUserActiveAsync(user.Id, false, Now.AddMinutes(6)).ConfigureAwait(false));
        Assert.False((await _store.FindUserByIdAsync(user.Id).ConfigureAwait(false))!.IsActive);
        Assert.True(await _store.SetUserActiveAsync(user.Id, true, Now.AddMinutes(7)).ConfigureAwait(false));
        Assert.True((await _store.FindUserByIdAsync(user.Id).ConfigureAwait(false))!.IsActive);

        Assert.False(await _store.ChangePasswordAsync(Guid.NewGuid(), "missing", Now.AddMinutes(8)).ConfigureAwait(false));
        Assert.False(await _store.SetUserActiveAsync(Guid.NewGuid(), false, Now.AddMinutes(8)).ConfigureAwait(false));
    }

    [Trait("Capability", "RefreshTokenContract")]
    [PostgresFact]
    public async Task RefreshTokenLimitsReplayAndRevocationPathsAreEnforced()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true).ConfigureAwait(false);
        Guid limitedFamily = Guid.NewGuid();
        RefreshTokenRecord first = CreateRefreshToken(user, limitedFamily, "limited-first", Now);
        RefreshTokenRecord second = CreateRefreshToken(user, limitedFamily, "limited-second", Now.AddSeconds(1));
        RefreshTokenRecord otherFamily = CreateRefreshToken(user, Guid.NewGuid(), "limited-other", Now.AddSeconds(2));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _store.TryCreateRefreshTokenAsync(first, 0, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _store.TryCreateRefreshTokenAsync(first, 1, 0));
        Assert.True(await _store.TryCreateRefreshTokenAsync(first, 1, 1).ConfigureAwait(false));
        Assert.False(await _store.TryCreateRefreshTokenAsync(second, 1, 1).ConfigureAwait(false));
        Assert.False(await _store.TryCreateRefreshTokenAsync(otherFamily, 1, 1).ConfigureAwait(false));

        Assert.False(await _store.RevokeRefreshTokenAsync(
            first.TokenHash,
            Guid.NewGuid(),
            allowAnyUser: false,
            revokeFamily: false,
            Now.AddMinutes(1)).ConfigureAwait(false));
        Assert.True(await _store.RevokeRefreshTokenAsync(
            first.TokenHash,
            user.Id,
            allowAnyUser: false,
            revokeFamily: false,
            Now.AddMinutes(1)).ConfigureAwait(false));
        Assert.True(await _store.RevokeRefreshTokenAsync(
            first.TokenHash,
            user.Id,
            allowAnyUser: false,
            revokeFamily: false,
            Now.AddMinutes(2)).ConfigureAwait(false));

        Guid replayFamily = Guid.NewGuid();
        RefreshTokenRecord replayed = CreateRefreshToken(user, replayFamily, "replay-existing", Now.AddMinutes(3));
        RefreshTokenRecord replacement = CreateRefreshToken(user, replayFamily, "replay-replacement", Now.AddMinutes(4));
        await _store.CreateRefreshTokenAsync(replayed).ConfigureAwait(false);
        Assert.Equal(
            TokenRotationStatus.Success,
            (await _store.RotateRefreshTokenAsync(
                replayed.TokenHash,
                replacement,
                Now.AddMinutes(5),
                20).ConfigureAwait(false)).Status);
        Assert.False(await _store.HandleRefreshTokenReplayAsync(
            replacement.TokenHash,
            Now.AddMinutes(6)).ConfigureAwait(false));
        Assert.False(await _store.HandleRefreshTokenReplayAsync(
            "missing-token",
            Now.AddMinutes(6)).ConfigureAwait(false));
        Assert.True(await _store.HandleRefreshTokenReplayAsync(
            replayed.TokenHash,
            Now.AddMinutes(7)).ConfigureAwait(false));
        Assert.Equal(
            Now.AddMinutes(7),
            (await _store.FindRefreshTokenByHashAsync(replacement.TokenHash).ConfigureAwait(false))!.RevokedUtc);

        Guid revocationFamily = Guid.NewGuid();
        RefreshTokenRecord familyFirst = CreateRefreshToken(user, revocationFamily, "family-first", Now.AddMinutes(8));
        RefreshTokenRecord familySecond = CreateRefreshToken(user, revocationFamily, "family-second", Now.AddMinutes(9));
        await _store.CreateRefreshTokenAsync(familyFirst).ConfigureAwait(false);
        await _store.CreateRefreshTokenAsync(familySecond).ConfigureAwait(false);
        Assert.True(await _store.RevokeRefreshTokenAsync(
            familyFirst.TokenHash,
            Guid.NewGuid(),
            allowAnyUser: true,
            revokeFamily: true,
            Now.AddMinutes(10)).ConfigureAwait(false));
        Assert.Equal(Now.AddMinutes(10), (await _store.FindRefreshTokenByHashAsync(familySecond.TokenHash).ConfigureAwait(false))!.RevokedUtc);

        RefreshTokenRecord remaining = CreateRefreshToken(user, Guid.NewGuid(), "remaining", Now.AddMinutes(11));
        await _store.CreateRefreshTokenAsync(remaining).ConfigureAwait(false);
        Assert.Equal(1, await _store.RevokeAllUserRefreshTokensAsync(user.Id, Now.AddMinutes(12)).ConfigureAwait(false));
        Assert.Equal(Now.AddMinutes(12), (await _store.FindRefreshTokenByHashAsync(remaining.TokenHash).ConfigureAwait(false))!.RevokedUtc);
    }

    [Trait("Capability", "AdministratorSeedContract")]
    [PostgresFact]
    public async Task AdministratorSeedCreatesThenRotatesVerifiedAdministrator()
    {
        AdminSeedOptions options = new()
        {
            Email = $"admin-{Guid.NewGuid():N}@example.com",
            Password = "unused-by-store"
        };
        await _store.SeedAdminAsync(options, "first-admin-hash", Now.AddMinutes(1)).ConfigureAwait(false);

        AuthUser seeded = (await _store.FindUserByNormalizedEmailAsync(options.Email.ToUpperInvariant()).ConfigureAwait(false))!;
        Assert.True(seeded.IsActive);
        Assert.True(seeded.EmailVerifiedUtc.HasValue);
        Assert.Equal("first-admin-hash", seeded.PasswordHash);
        EffectiveAuthorizationContext authorization = await _store.GetEffectiveAuthorizationContextAsync(seeded.Id, null).ConfigureAwait(false);
        Assert.Contains(AuthRoles.Admin, authorization.Global.Roles);

        RefreshTokenRecord session = CreateRefreshToken(seeded, Guid.NewGuid(), "admin-session", Now.AddMinutes(2));
        await _store.CreateRefreshTokenAsync(session).ConfigureAwait(false);
        await _store.SeedAdminAsync(options, "second-admin-hash", Now.AddMinutes(3)).ConfigureAwait(false);

        AuthUser rotated = (await _store.FindUserByIdAsync(seeded.Id).ConfigureAwait(false))!;
        Assert.Equal("second-admin-hash", rotated.PasswordHash);
        Assert.True(rotated.SecurityVersion > seeded.SecurityVersion);
        Assert.Equal(Now.AddMinutes(3), (await _store.FindRefreshTokenByHashAsync(session.TokenHash).ConfigureAwait(false))!.RevokedUtc);
        Assert.Contains(
            AuthRoles.Admin,
            (await _store.GetEffectiveAuthorizationContextAsync(rotated.Id, null).ConfigureAwait(false)).Global.Roles);
    }

    [Trait("Capability", "GlobalAuthorizationContract")]
    [PostgresFact]
    public async Task GlobalAuthorizationMutationLifecycleCoversConflictsAndRemovalPaths()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true).ConfigureAwait(false);
        string normalizedName = $"COVERAGE-{Guid.NewGuid():N}";
        RoleRecord role = (await _store.CreateRoleAsync(
            "Coverage Role",
            normalizedName,
            "Coverage role.",
            Now.AddMinutes(1)).ConfigureAwait(false))!;
        Assert.Null(await _store.CreateRoleAsync(
            "Duplicate Coverage Role",
            normalizedName,
            "Duplicate.",
            Now.AddMinutes(2)).ConfigureAwait(false));

        Assert.True(await _store.UpdateRoleAsync(
            role.Id,
            "Updated Coverage Role",
            normalizedName + "-UPDATED",
            "Updated.",
            Now.AddMinutes(3)).ConfigureAwait(false));
        Assert.False(await _store.UpdateRoleAsync(
            Guid.NewGuid(),
            "Missing",
            "MISSING",
            "Missing.",
            Now.AddMinutes(3)).ConfigureAwait(false));

        RoleRecord systemRole = (await _store.ListRolesAsync(new AuthPageQuery(200, null)).ConfigureAwait(false)).Items
            .First(static item => item.IsSystem);
        Assert.False(await _store.UpdateRoleAsync(
            systemRole.Id,
            "Changed System",
            "CHANGED-SYSTEM",
            "Not allowed.",
            Now.AddMinutes(3)).ConfigureAwait(false));

        PermissionRecord permission = (await _store.ListPermissionsAsync(new AuthPageQuery(200, null)).ConfigureAwait(false)).Items
            .Single(static item => item.Name == AuthPermissions.AuditRead);
        RefreshTokenRecord session = CreateRefreshToken(user, Guid.NewGuid(), "authorization-session", Now.AddMinutes(3));
        await _store.CreateRefreshTokenAsync(session).ConfigureAwait(false);

        Assert.True(await _store.AssignPermissionToRoleAsync(role.Id, permission.Id, Now.AddMinutes(4)).ConfigureAwait(false));
        Assert.False(await _store.AssignPermissionToRoleAsync(role.Id, permission.Id, Now.AddMinutes(4)).ConfigureAwait(false));
        Assert.True(await _store.AssignGlobalRoleToUserAsync(user.Id, role.Id, Now.AddMinutes(5)).ConfigureAwait(false));
        Assert.False(await _store.AssignGlobalRoleToUserAsync(user.Id, role.Id, Now.AddMinutes(5)).ConfigureAwait(false));
        Assert.NotNull((await _store.FindRefreshTokenByHashAsync(session.TokenHash).ConfigureAwait(false))!.RevokedUtc);

        EffectiveAuthorizationContext assigned = await _store.GetEffectiveAuthorizationContextAsync(user.Id, null).ConfigureAwait(false);
        Assert.Contains("Updated Coverage Role", assigned.Global.Roles);
        Assert.Contains(AuthPermissions.AuditRead, assigned.Global.Permissions);

        Assert.True(await _store.RemovePermissionFromRoleAsync(role.Id, permission.Id, Now.AddMinutes(6)).ConfigureAwait(false));
        Assert.False(await _store.RemovePermissionFromRoleAsync(role.Id, permission.Id, Now.AddMinutes(6)).ConfigureAwait(false));
        Assert.True(await _store.RemoveGlobalRoleFromUserAsync(user.Id, role.Id, Now.AddMinutes(7)).ConfigureAwait(false));
        Assert.False(await _store.RemoveGlobalRoleFromUserAsync(user.Id, role.Id, Now.AddMinutes(7)).ConfigureAwait(false));
        Assert.False(await _store.AssignGlobalRoleToUserAsync(user.Id, Guid.NewGuid(), Now.AddMinutes(8)).ConfigureAwait(false));

        EffectiveAuthorizationContext removed = await _store.GetEffectiveAuthorizationContextAsync(user.Id, null).ConfigureAwait(false);
        Assert.DoesNotContain("Updated Coverage Role", removed.Global.Roles);
        Assert.DoesNotContain(AuthPermissions.AuditRead, removed.Global.Permissions);
    }

    [Trait("Capability", "TenantAuthorizationContract")]
    [PostgresFact]
    public async Task TenantMembershipRoleAndOwnershipPathsRemainScoped()
    {
        AuthUser owner = await CreateUserAsync(emailVerified: true).ConfigureAwait(false);
        AuthUser member = await CreateUserAsync(emailVerified: true).ConfigureAwait(false);
        AuthUser outsider = await CreateUserAsync(emailVerified: true).ConfigureAwait(false);
        TenantRecord tenant = (await _store.CreateTenantAsync(
            "Coverage Tenant",
            $"coverage-{Guid.NewGuid():N}",
            owner.Id,
            Now.AddMinutes(1)).ConfigureAwait(false))!;

        Assert.Equal(tenant.Id, (await _store.FindTenantAsync(tenant.Id).ConfigureAwait(false))!.Id);
        Assert.True(await _store.IsTenantMemberAsync(owner.Id, tenant.Id).ConfigureAwait(false));
        Assert.True(await _store.AddTenantMemberAsync(tenant.Id, member.Id, Now.AddMinutes(2)).ConfigureAwait(false));
        Assert.False(await _store.AddTenantMemberAsync(tenant.Id, member.Id, Now.AddMinutes(2)).ConfigureAwait(false));
        Assert.Contains(
            (await _store.ListTenantsForUserAsync(member.Id, new AuthPageQuery(20, null)).ConfigureAwait(false)).Items,
            item => item.Id == tenant.Id);

        Assert.True(await _store.AssignTenantRoleToUserAsync(
            tenant.Id,
            member.Id,
            TenantManagerRoleId,
            Now.AddMinutes(3)).ConfigureAwait(false));
        Assert.False(await _store.AssignTenantRoleToUserAsync(
            tenant.Id,
            member.Id,
            TenantManagerRoleId,
            Now.AddMinutes(3)).ConfigureAwait(false));
        EffectiveAuthorizationContext manager = await _store.GetEffectiveAuthorizationContextAsync(member.Id, tenant.Id).ConfigureAwait(false);
        Assert.Contains(TenantAuthRoles.Manager, manager.Tenant!.Roles);
        Assert.True(await _store.RemoveTenantRoleFromUserAsync(
            tenant.Id,
            member.Id,
            TenantManagerRoleId,
            Now.AddMinutes(4)).ConfigureAwait(false));
        Assert.False(await _store.RemoveTenantRoleFromUserAsync(
            tenant.Id,
            member.Id,
            TenantManagerRoleId,
            Now.AddMinutes(4)).ConfigureAwait(false));

        Assert.Equal(
            TenantOwnershipTransferStatus.SameOwner,
            (await _store.TransferTenantOwnershipAsync(
                tenant.Id,
                owner.Id,
                owner.Id,
                Now.AddMinutes(5)).ConfigureAwait(false)).Status);
        Assert.Equal(
            TenantOwnershipTransferStatus.NewOwnerNotMember,
            (await _store.TransferTenantOwnershipAsync(
                tenant.Id,
                owner.Id,
                outsider.Id,
                Now.AddMinutes(6)).ConfigureAwait(false)).Status);
        Assert.Equal(
            TenantOwnershipTransferStatus.CurrentOwnerMismatch,
            (await _store.TransferTenantOwnershipAsync(
                tenant.Id,
                member.Id,
                owner.Id,
                Now.AddMinutes(7)).ConfigureAwait(false)).Status);
        Assert.Equal(
            TenantOwnershipTransferStatus.Success,
            (await _store.TransferTenantOwnershipAsync(
                tenant.Id,
                owner.Id,
                member.Id,
                Now.AddMinutes(8)).ConfigureAwait(false)).Status);
        Assert.Equal(member.Id, (await _store.GetTenantOwnerAsync(tenant.Id).ConfigureAwait(false))!.UserId);
    }

    [Trait("Capability", "ProviderInfrastructureContract")]
    [PostgresFact]
    public async Task ProviderInfrastructureCoordinatesCommandsTransactionsAndErrorClassification()
    {
        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        PostgresAuthCommandFactory commandFactory = new();
        await using (DbCommand command = commandFactory.Create(connection, null, "SELECT 1;"))
        {
            Assert.Equal("SELECT 1;", command.CommandText);
            Assert.Equal(
                1,
                Convert.ToInt32(
                    await command.ExecuteScalarAsync().ConfigureAwait(false),
                    CultureInfo.InvariantCulture));
        }
        Assert.Throws<ArgumentNullException>(() => commandFactory.Create(null!, null, "SELECT 1;"));
        Assert.Throws<ArgumentException>(() => commandFactory.Create(connection, null, ""));

        PostgresAuthTransactionManager transactions = new();
        int committed = await transactions.ExecuteAsync(
            connection,
            IsolationLevel.ReadCommitted,
            async (transaction, cancellationToken) =>
            {
                await using NpgsqlCommand command = new("SELECT 7;", connection, (NpgsqlTransaction)transaction);
                return Convert.ToInt32(
                    await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
            }).ConfigureAwait(false);
        Assert.Equal(7, committed);

        InvalidOperationException expected = new("transaction-failure");
        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transactions.ExecuteAsync<int>(
                connection,
                IsolationLevel.Serializable,
                (_, _) => Task.FromException<int>(expected)));
        Assert.Same(expected, actual);

        Assert.Equal(AuthDatabaseErrorCategory.ConnectionFailure, PostgresAuthDatabaseErrorClassifier.ClassifySqlState("08006"));
        Assert.Equal(AuthDatabaseErrorCategory.UniqueConstraint, PostgresAuthDatabaseErrorClassifier.ClassifySqlState("23505"));
        Assert.Equal(AuthDatabaseErrorCategory.ForeignKeyConstraint, PostgresAuthDatabaseErrorClassifier.ClassifySqlState("23503"));
        Assert.Equal(AuthDatabaseErrorCategory.PermissionDenied, PostgresAuthDatabaseErrorClassifier.ClassifySqlState("42501"));
        Assert.Equal(AuthDatabaseErrorCategory.SchemaMismatch, PostgresAuthDatabaseErrorClassifier.ClassifySqlState("42P01"));
        Assert.Equal(AuthDatabaseErrorCategory.Unknown, PostgresAuthDatabaseErrorClassifier.ClassifySqlState("99999"));
    }

    private async Task<AuthUser> CreateUserAsync(bool emailVerified, string? email = null)
    {
        Guid id = Guid.NewGuid();
        string effectiveEmail = email ?? $"coverage-{id:N}@example.com";
        AuthUser user = new(
            id,
            effectiveEmail,
            effectiveEmail.ToUpperInvariant(),
            "hash",
            EmailVerifiedUtc: emailVerified ? Now : null,
            IsActive: true,
            FailedLoginAttempts: 0,
            LockoutEndUtc: null,
            SecurityVersion: 1,
            CreatedUtc: Now,
            UpdatedUtc: Now);
        Assert.True(await _store.CreateUserWithVerificationTokenAsync(
            user,
            $"verification-{Guid.NewGuid():N}",
            Now.AddHours(1)).ConfigureAwait(false));
        return user;
    }

    private static RefreshTokenRecord CreateRefreshToken(
        AuthUser user,
        Guid familyId,
        string prefix,
        DateTimeOffset createdUtc) =>
        new(
            Guid.NewGuid(),
            user.Id,
            $"{prefix}-{Guid.NewGuid():N}",
            familyId,
            user.SecurityVersion,
            "127.0.0.1",
            "postgres-coverage-contract",
            createdUtc,
            createdUtc,
            createdUtc.AddDays(30),
            RevokedUtc: null,
            ReplacedByTokenId: null);
}
