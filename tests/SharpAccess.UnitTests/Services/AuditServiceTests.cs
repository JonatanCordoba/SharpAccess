using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Services;

namespace SharpAccess.UnitTests;

public sealed class AuditServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsyncTruncatesLongMetadataAndPreservesWholeSurrogatePairs()
    {
        CapturingAuthStore store = new();
        FakeClock clock = new(FixedUtcNow);
        AuditService service = new(store, clock);
        string ipAddress = new('1', 100);
        string userAgent = new string('a', 511) + char.ConvertFromUtf32(0x1F600);
        string detail = new('d', 1_100);

        await service.WriteAsync(
            "user.login",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ipAddress,
            userAgent,
            detail);

        Assert.NotNull(store.Audit);
        AuditRecord audit = store.Audit;
        Assert.Equal(clock.UtcNow, audit.CreatedUtc);
        Assert.Equal("user.login", audit.EventType);
        Assert.Equal(64, audit.IpAddress!.Length);
        Assert.Equal(511, audit.UserAgent!.Length);
        Assert.False(char.IsHighSurrogate(audit.UserAgent[^1]));
        Assert.Equal(1_024, audit.Detail!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteAsyncStoresNullForBlankMetadata(string? value)
    {
        CapturingAuthStore store = new();
        AuditService service = new(store, new FakeClock(FixedUtcNow));

        await service.WriteAsync("user.logout", null, null, value, value, value);

        Assert.NotNull(store.Audit);
        AuditRecord audit = store.Audit;
        Assert.Null(audit.IpAddress);
        Assert.Null(audit.UserAgent);
        Assert.Null(audit.Detail);
    }

    [Fact]
    public async Task WriteAsyncRejectsBlankEventTypes()
    {
        AuditService service = new(new CapturingAuthStore(), new FakeClock(FixedUtcNow));

        await Assert.ThrowsAsync<ArgumentException>(() => service.WriteAsync(" ", null, null, null, null, null));
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IAuthClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class CapturingAuthStore : IAuthAuditStore
    {
        public AuditRecord Audit { get; private set; } = default!;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MigrateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ValidateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SharpAccessSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GenerateScriptAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CreateUserWithVerificationTokenAsync(AuthUser user, string verificationTokenHash, DateTimeOffset verificationExpiresUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthUser?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthPageSlice<AuthUser>> ListUsersAsync(AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordLoginFailureAsync(Guid userId, int failureThreshold, DateTimeOffset lockoutEndUtc, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetLoginFailuresAsync(Guid userId, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdatePasswordHashAsync(Guid userId, string passwordHash, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdatePasswordHashAsync(Guid userId, string passwordHash, int passwordHashVersion, string passwordHashAlgorithm, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReplaceOneTimeTokenAsync(Guid userId, string purpose, string tokenHash, DateTimeOffset createdUtc, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> VerifyEmailAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> ResetPasswordAsync(string tokenHash, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EffectiveAuthorizationContext> GetEffectiveAuthorizationContextAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsTenantMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateRefreshTokenAsync(RefreshTokenRecord token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCreateRefreshTokenAsync(RefreshTokenRecord token, int maximumActiveFamiliesPerUser, int maximumActiveTokensPerFamily, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefreshTokenRecord?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TokenRotationResult> RotateRefreshTokenAsync(string existingTokenHash, RefreshTokenRecord replacement, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TokenRotationResult> RotateRefreshTokenAsync(string existingTokenHash, RefreshTokenRecord replacement, DateTimeOffset now, int maximumActiveTokensPerFamily, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RevokeRefreshTokenAsync(string tokenHash, Guid requestingUserId, bool allowAnyUser, bool revokeFamily, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RevokeRefreshTokenFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveOAuthStateAsync(OAuthStateRecord state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OAuthStateRecord?> ConsumeOAuthStateAsync(string provider, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(string provider, string providerSubject, string email, string normalizedEmail, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(string provider, string providerSubject, string email, string normalizedEmail, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CreateOneTimeTokenAsync(Guid userId, string purpose, string tokenHash, DateTimeOffset createdUtc, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OneTimeTokenRecord?> ConsumeOneTimeTokenAsync(string purpose, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthPageSlice<TenantRecord>> ListTenantsForUserAsync(Guid userId, AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantRecord?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantOwnerRecord?> GetTenantOwnerAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthPageSlice<TenantMemberRecord>> ListTenantMembersAsync(Guid tenantId, AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task WriteAuditAsync(AuditRecord audit, CancellationToken cancellationToken = default)
        {
            Audit = audit;
            return Task.CompletedTask;
        }

        public Task<AuthPageSlice<AuditRecord>> ListAuditAsync(AuthPageQuery page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SeedAdminAsync(AdminSeedOptions options, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
