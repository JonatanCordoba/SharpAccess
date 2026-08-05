using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using SharpAccess.Services;
using SharpAccess.Tokens;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class RefreshSessionUseCaseTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FamilyId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // Verifies that refresh response material is prepared before the irreversible rotation attempt.
    [Trait("MutationInvariant", "RefreshRotation")]
    [Fact]
    public async Task RefreshAsyncPreparesTheResponseBeforeRotation()
    {
        CapturingAuthStore store = new(TokenRotationStatus.Expired);
        CapturingSessionIssuer sessions = new();
        RefreshSessionUseCase useCase = new(
            store,
            new FakeTokenProtector(),
            new FakeClock(FixedUtcNow),
            sessions,
            Options.Create(TestOptions.Create()));

        ServiceResult<SessionTokens> result = await useCase.RefreshAsync(
            "refresh-token",
            null,
            new RequestMetadata("127.0.0.1", "unit-test"));

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.Unauthorized, result.Error);
        Assert.Equal(1, sessions.BuildContextCalls);
        Assert.Equal(1, sessions.CreateAccessTokenCalls);
        Assert.Equal(1, sessions.CreateRefreshTokenCalls);
        Assert.True(store.RotateCalled);
    }

    // Verifies that context-construction failures leave the existing refresh token unrotated.
    [Trait("MutationInvariant", "RefreshRotation")]
    [Fact]
    public async Task RefreshAsyncDoesNotRotateWhenContextConstructionFails()
    {
        CapturingAuthStore store = new(TokenRotationStatus.Success);
        CapturingSessionIssuer sessions = new() { FailContextConstruction = true };
        RefreshSessionUseCase useCase = new(
            store,
            new FakeTokenProtector(),
            new FakeClock(FixedUtcNow),
            sessions,
            Options.Create(TestOptions.Create()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.RefreshAsync(
            "refresh-token",
            null,
            new RequestMetadata("127.0.0.1", "unit-test")));

        Assert.Equal(1, sessions.BuildContextCalls);
        Assert.Equal(0, sessions.CreateAccessTokenCalls);
        Assert.Equal(0, sessions.CreateRefreshTokenCalls);
        Assert.False(store.RotateCalled);
    }

    // Verifies that replay handling precedes tenant preflight and preserves requested-tenant evidence.
    [Trait("MutationInvariant", "RefreshReplay")]
    [Fact]
    public async Task RevokedRefreshTokenIsHandledBeforeInvalidTenantPreflight()
    {
        CapturingAuthStore store = new(TokenRotationStatus.Success, existingRevoked: true);
        CapturingSessionIssuer sessions = new();
        RefreshSessionUseCase useCase = new(
            store,
            new FakeTokenProtector(),
            new FakeClock(FixedUtcNow),
            sessions,
            Options.Create(TestOptions.Create()));
        Guid requestedTenant = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        ServiceResult<SessionTokens> result = await useCase.RefreshAsync(
            "refresh-token",
            requestedTenant,
            new RequestMetadata("127.0.0.1", "unit-test"));

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.Unauthorized, result.Error);
        Assert.True(store.ReplayHandled);
        Assert.Equal(0, store.FindUserCalls);
        Assert.Equal(0, store.TenantMembershipChecks);
        Assert.Equal(0, sessions.BuildContextCalls);
        Assert.Null(store.ReplayAudit!.TenantId);
        Assert.Contains($"requested_tenant={requestedTenant:D}", store.ReplayAudit.Detail, StringComparison.Ordinal);
    }

    // Verifies that replay handling precedes session-context construction.
    [Trait("MutationInvariant", "RefreshReplay")]
    [Fact]
    public async Task RevokedRefreshTokenIsHandledBeforeContextConstruction()
    {
        CapturingAuthStore store = new(TokenRotationStatus.Success, existingRevoked: true);
        CapturingSessionIssuer sessions = new() { FailContextConstruction = true };
        RefreshSessionUseCase useCase = new(
            store,
            new FakeTokenProtector(),
            new FakeClock(FixedUtcNow),
            sessions,
            Options.Create(TestOptions.Create()));

        ServiceResult<SessionTokens> result = await useCase.RefreshAsync(
            "refresh-token",
            null,
            new RequestMetadata("127.0.0.1", "unit-test"));

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.Unauthorized, result.Error);
        Assert.True(store.ReplayHandled);
        Assert.Equal(0, store.FindUserCalls);
        Assert.Equal(0, sessions.BuildContextCalls);
        Assert.False(store.RotateCalled);
    }

    // Verifies that access-token construction failures leave the existing refresh token unrotated.
    [Trait("MutationInvariant", "RefreshRotation")]
    [Fact]
    public async Task RefreshAsyncDoesNotRotateWhenAccessTokenConstructionFails()
    {
        CapturingAuthStore store = new(TokenRotationStatus.Success);
        CapturingSessionIssuer sessions = new() { FailAccessTokenConstruction = true };
        RefreshSessionUseCase useCase = new(
            store,
            new FakeTokenProtector(),
            new FakeClock(FixedUtcNow),
            sessions,
            Options.Create(TestOptions.Create()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.RefreshAsync(
            "refresh-token",
            null,
            new RequestMetadata("127.0.0.1", "unit-test")));

        Assert.Equal(1, sessions.BuildContextCalls);
        Assert.Equal(1, sessions.CreateAccessTokenCalls);
        Assert.Equal(0, sessions.CreateRefreshTokenCalls);
        Assert.False(store.RotateCalled);
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IAuthClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeTokenProtector : ITokenProtector
    {
        public string Generate(int byteLength = 48) => "generated-refresh-token";
        public string Hash(string rawToken) => "hash:" + rawToken;
    }

    private sealed class CapturingAuditService : IAuditService
    {
        public Task WriteAsync(
            string eventType,
            Guid? userId,
            Guid? tenantId,
            string? ipAddress,
            string? userAgent,
            string? detail,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CapturingSessionIssuer : IAuthSessionIssuer
    {
        public bool FailContextConstruction { get; init; }
        public bool FailAccessTokenConstruction { get; init; }
        public int BuildContextCalls { get; private set; }
        public int CreateAccessTokenCalls { get; private set; }
        public int CreateRefreshTokenCalls { get; private set; }

        public Task<ServiceResult<SessionTokens>> IssueSessionAsync(
            AuthUser user,
            Guid? tenantId,
            Guid? familyId,
            RequestMetadata metadata,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UserContext> BuildContextAsync(
            AuthUser user,
            Guid? tenantId,
            CancellationToken cancellationToken)
        {
            BuildContextCalls++;
            if (FailContextConstruction)
            {
                throw new InvalidOperationException("Context construction failed before rotation.");
            }

            EffectiveAuthorizationContext authorization = new(
                new GlobalAuthorizationContext([], []),
                tenantId.HasValue
                    ? new TenantAuthorizationContext(tenantId.Value, false, [], [])
                    : null,
                user.SecurityVersion);
            return Task.FromResult(new UserContext(
                user.Id,
                user.Email,
                true,
                authorization,
                user.SecurityVersion));
        }

        public AccessTokenResult CreateAccessToken(UserContext context)
        {
            CreateAccessTokenCalls++;
            if (FailAccessTokenConstruction)
            {
                throw new InvalidOperationException("Access-token construction failed before rotation.");
            }

            return new AccessTokenResult("access-token", FixedUtcNow.AddMinutes(5));
        }

        public (string RawToken, RefreshTokenRecord Record) CreateRefreshToken(
            AuthUser user,
            Guid familyId,
            RequestMetadata metadata,
            DateTimeOffset now)
        {
            CreateRefreshTokenCalls++;
            return (
                "replacement-token",
                new RefreshTokenRecord(
                    Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    user.Id,
                    "hash:replacement-token",
                    familyId,
                    user.SecurityVersion,
                    metadata.IpAddress,
                    metadata.UserAgent,
                    now,
                    now.AddDays(30),
                    now.AddDays(30),
                    null,
                    null));
        }
    }

    private sealed class CapturingAuthStore(TokenRotationStatus rotationStatus, bool existingRevoked = false) : IAuthStore
    {
        private readonly AuthUser _user = new(
            UserId,
            "user@test.local",
            "USER@TEST.LOCAL",
            "password-hash",
            FixedUtcNow,
            true,
            0,
            null,
            1,
            FixedUtcNow,
            FixedUtcNow);

        private readonly RefreshTokenRecord _existing = new(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            UserId,
            "hash:refresh-token",
            FamilyId,
            1,
            "127.0.0.1",
            "unit-test",
            FixedUtcNow.AddDays(-1),
            FixedUtcNow.AddDays(1),
            FixedUtcNow.AddDays(1),
            existingRevoked ? FixedUtcNow.AddMinutes(-1) : null,
            null);

        public bool RotateCalled { get; private set; }
        public bool ReplayHandled { get; private set; }
        public AuditRecord? ReplayAudit { get; private set; }
        public int FindUserCalls { get; private set; }
        public int TenantMembershipChecks { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MigrateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ValidateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SharpAccessSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GenerateScriptAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CreateUserWithVerificationTokenAsync(AuthUser user, string verificationTokenHash, DateTimeOffset verificationExpiresUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthUser?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Records refresh-user lookups and returns the configured user.
        public Task<AuthUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            FindUserCalls++;
            return Task.FromResult<AuthUser?>(_user);
        }
        public Task<AuthPageSlice<AuthUser>> ListUsersAsync(AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordLoginFailureAsync(Guid userId, int failureThreshold, DateTimeOffset lockoutEndUtc, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetLoginFailuresAsync(Guid userId, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdatePasswordHashAsync(Guid userId, string passwordHash, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdatePasswordHashAsync(Guid userId, string passwordHash, int passwordHashVersion, string passwordHashAlgorithm, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReplaceOneTimeTokenAsync(Guid userId, string purpose, string tokenHash, DateTimeOffset createdUtc, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> VerifyEmailAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> ResetPasswordAsync(string tokenHash, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CreateOneTimeTokenAsync(Guid userId, string purpose, string tokenHash, DateTimeOffset createdUtc, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OneTimeTokenRecord?> ConsumeOneTimeTokenAsync(string purpose, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateRefreshTokenAsync(RefreshTokenRecord token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCreateRefreshTokenAsync(RefreshTokenRecord token, int maximumActiveFamiliesPerUser, int maximumActiveTokensPerFamily, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefreshTokenRecord?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default) => Task.FromResult<RefreshTokenRecord?>(_existing);
        // Records replay handling and its atomically supplied audit evidence.
        public Task<bool> HandleRefreshTokenReplayAsync(string tokenHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default)
        {
            ReplayHandled = true;
            ReplayAudit = audit;
            return Task.FromResult(true);
        }

        public Task<TokenRotationResult> RotateRefreshTokenAsync(
            string existingTokenHash,
            RefreshTokenRecord replacement,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            RotateCalled = true;
            return Task.FromResult(new TokenRotationResult(rotationStatus, UserId, FamilyId));
        }


        public Task<TokenRotationResult> RotateRefreshTokenAsync(
            string existingTokenHash,
            RefreshTokenRecord replacement,
            DateTimeOffset now,
            int maximumActiveTokensPerFamily,
            CancellationToken cancellationToken = default)
        {
            _ = maximumActiveTokensPerFamily;
            return RotateRefreshTokenAsync(
                existingTokenHash,
                replacement,
                now,
                cancellationToken);
        }
        public Task<bool> RevokeRefreshTokenAsync(string tokenHash, Guid requestingUserId, bool allowAnyUser, bool revokeFamily, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RevokeRefreshTokenFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveOAuthStateAsync(OAuthStateRecord state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OAuthStateRecord?> ConsumeOAuthStateAsync(string provider, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(string provider, string providerSubject, string email, string normalizedEmail, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(string provider, string providerSubject, string email, string normalizedEmail, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EffectiveAuthorizationContext> GetEffectiveAuthorizationContextAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthPageSlice<RoleRecord>> ListRolesAsync(AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RoleRecord?> CreateRoleAsync(string name, string normalizedName, string description, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateRoleAsync(Guid roleId, string name, string normalizedName, string description, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthPageSlice<PermissionRecord>> ListPermissionsAsync(AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemovePermissionFromRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AssignGlobalRoleToUserAsync(Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveGlobalRoleFromUserAsync(Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AssignTenantRoleToUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveTenantRoleFromUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Records tenant-membership preflight and returns a nonmember result.
        public Task<bool> IsTenantMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
        {
            TenantMembershipChecks++;
            return Task.FromResult(false);
        }
        public Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthPageSlice<TenantRecord>> ListTenantsForUserAsync(Guid userId, AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantRecord?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantOwnerRecord?> GetTenantOwnerAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthPageSlice<TenantMemberRecord>> ListTenantMembersAsync(Guid tenantId, AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WriteAuditAsync(AuditRecord audit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthPageSlice<AuditRecord>> ListAuditAsync(AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Delegates an evidence-bearing password change for this refresh test double.
        public Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => ChangePasswordAsync(userId, passwordHash, now, cancellationToken);
        // Delegates an evidence-bearing user status change for this refresh test double.
        public Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => SetUserActiveAsync(userId, isActive, now, cancellationToken);
        // Delegates evidence-bearing email verification for this refresh test double.
        public Task<Guid?> VerifyEmailAsync(string tokenHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => VerifyEmailAsync(tokenHash, now, cancellationToken);
        // Delegates an evidence-bearing password reset for this refresh test double.
        public Task<Guid?> ResetPasswordAsync(string tokenHash, string passwordHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => ResetPasswordAsync(tokenHash, passwordHash, now, cancellationToken);
        // Delegates evidence-bearing refresh rotation for this refresh test double.
        public Task<TokenRotationResult> RotateRefreshTokenAsync(string existingTokenHash, RefreshTokenRecord replacement, DateTimeOffset now, int maximumActiveTokensPerFamily, RefreshTokenAuditEvidence audit, CancellationToken cancellationToken = default) => RotateRefreshTokenAsync(existingTokenHash, replacement, now, maximumActiveTokensPerFamily, cancellationToken);
        // Delegates evidence-bearing refresh revocation for this refresh test double.
        public Task<bool> RevokeRefreshTokenAsync(string tokenHash, Guid requestingUserId, bool allowAnyUser, bool revokeFamily, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RevokeRefreshTokenAsync(tokenHash, requestingUserId, allowAnyUser, revokeFamily, now, cancellationToken);
        // Delegates evidence-bearing refresh-family revocation for this refresh test double.
        public Task<int> RevokeRefreshTokenFamilyAsync(Guid familyId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RevokeRefreshTokenFamilyAsync(familyId, now, cancellationToken);
        // Delegates evidence-bearing user-wide refresh revocation for this refresh test double.
        public Task<int> RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RevokeAllUserRefreshTokensAsync(userId, now, cancellationToken);
        // Delegates evidence-bearing role creation for this refresh test double.
        public Task<RoleRecord?> CreateRoleAsync(string name, string normalizedName, string description, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => CreateRoleAsync(name, normalizedName, description, now, cancellationToken);
        // Delegates evidence-bearing role updates for this refresh test double.
        public Task<bool> UpdateRoleAsync(Guid roleId, string name, string normalizedName, string description, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => UpdateRoleAsync(roleId, name, normalizedName, description, now, cancellationToken);
        // Delegates evidence-bearing permission assignments for this refresh test double.
        public Task<bool> AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => AssignPermissionToRoleAsync(roleId, permissionId, now, cancellationToken);
        // Delegates evidence-bearing permission removals for this refresh test double.
        public Task<bool> RemovePermissionFromRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RemovePermissionFromRoleAsync(roleId, permissionId, now, cancellationToken);
        // Delegates evidence-bearing global role assignments for this refresh test double.
        public Task<bool> AssignGlobalRoleToUserAsync(Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => AssignGlobalRoleToUserAsync(userId, roleId, now, cancellationToken);
        // Delegates evidence-bearing global role removals for this refresh test double.
        public Task<bool> RemoveGlobalRoleFromUserAsync(Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RemoveGlobalRoleFromUserAsync(userId, roleId, now, cancellationToken);
        // Delegates evidence-bearing tenant role assignments for this refresh test double.
        public Task<bool> AssignTenantRoleToUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => AssignTenantRoleToUserAsync(tenantId, userId, roleId, now, cancellationToken);
        // Delegates evidence-bearing tenant role removals for this refresh test double.
        public Task<bool> RemoveTenantRoleFromUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RemoveTenantRoleFromUserAsync(tenantId, userId, roleId, now, cancellationToken);
        // Delegates evidence-bearing tenant creation for this refresh test double.
        public Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => CreateTenantAsync(name, slug, ownerUserId, now, cancellationToken);
        // Delegates evidence-bearing ownership transfer for this refresh test double.
        public Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => TransferTenantOwnershipAsync(tenantId, currentOwnerUserId, newOwnerUserId, now, cancellationToken);
        // Delegates evidence-bearing tenant membership creation for this refresh test double.
        public Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => AddTenantMemberAsync(tenantId, userId, now, cancellationToken);
        public Task SeedAdminAsync(AdminSeedOptions options, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects evidence-bearing admin seeding in this refresh test double.
        public Task SeedAdminAsync(AdminSeedOptions options, string passwordHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
