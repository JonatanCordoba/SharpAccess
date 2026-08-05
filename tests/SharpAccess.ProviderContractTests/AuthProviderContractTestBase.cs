using SharpAccess;
using SharpAccess.Domain;
using SharpAccess.Persistence;

namespace SharpAccess.ProviderContractTests;

public abstract class AuthProviderContractTestBase : IAsyncLifetime
{
    private static readonly string[] ExpectedBuiltInRoles =
    [
        AuthRoles.Admin,
        AuthRoles.User,
        AuthRoles.Manager
    ];

    private static readonly string[] ExpectedReplacementPasswordHashes =
    [
        "replacement-one",
        "replacement-two"
    ];

    private const int MaximumActiveRefreshTokensPerFamily = 20;

    private IAuthStore _store = null!;

    protected static DateTimeOffset Now { get; } = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    // Creates one provider-specific store instance for the concrete contract test class.
    public async Task InitializeAsync()
    {
        _store = RequireAuthStore(await CreateProviderStoreAsync().ConfigureAwait(false));
    }

    // Disposes provider-specific resources created by the concrete contract test class.
    public Task DisposeAsync() => DisposeProviderResourcesAsync();

    // Verifies that provider initialization is idempotent and seeds the authorization catalog once.
    protected async Task InitializationIsIdempotentAndSeedsAuthorizationCatalogOnceCore()
    {
        await InitializeStoreAsync();
        await InitializeStoreAsync();

        IReadOnlyList<RoleRecord> roles = (await _store.ListRolesAsync(new AuthPageQuery(200, null))).Items;
        IReadOnlyList<PermissionRecord> permissions = (await _store.ListPermissionsAsync(new AuthPageQuery(200, null))).Items;

        Assert.Equal(ExpectedBuiltInRoles.Order(StringComparer.Ordinal), roles.Select(role => role.Name).Order(StringComparer.Ordinal));
        Assert.Equal(AuthPermissions.All.Order(StringComparer.Ordinal), permissions.Select(permission => permission.Name).Order(StringComparer.Ordinal));
        Assert.All(roles, static role => Assert.True(role.IsSystem));
    }

    // Verifies that concurrent provider initialization remains safe and deterministic.
    protected async Task ConcurrentInitializationIsSafeAndDeterministicCore()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => InitializeStoreAsync()));

        IReadOnlyList<RoleRecord> roles = (await _store.ListRolesAsync(new AuthPageQuery(200, null))).Items;
        IReadOnlyList<PermissionRecord> permissions = (await _store.ListPermissionsAsync(new AuthPageQuery(200, null))).Items;

        Assert.Equal(ExpectedBuiltInRoles.Length, roles.Count);
        Assert.Equal(AuthPermissions.All.Count, permissions.Count);
    }

    // Verifies that a persisted refresh token can be retrieved by its keyed hash.
    protected async Task RefreshTokenCanBeCreatedAndFoundByHashCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        RefreshTokenRecord token = CreateRefreshToken(user);

        await _store.CreateRefreshTokenAsync(token);

        RefreshTokenRecord? found = await _store.FindRefreshTokenByHashAsync(token.TokenHash);
        Assert.NotNull(found);
        Assert.Equal(token.Id, found.Id);
        Assert.Equal(user.Id, found.UserId);
        Assert.Equal(token.TokenHash, found.TokenHash);
        Assert.Equal(token.FamilyId, found.FamilyId);
        Assert.Equal(token.SecurityVersion, found.SecurityVersion);
        Assert.Equal(token.IpAddress, found.IpAddress);
        Assert.Equal(token.UserAgent, found.UserAgent);
        Assert.Equal(token.AuthenticatedUtc, found.AuthenticatedUtc);
        Assert.Equal(token.CreatedUtc, found.CreatedUtc);
        Assert.Equal(token.ExpiresUtc, found.ExpiresUtc);
        Assert.Null(found.RevokedUtc);
        Assert.Null(found.ReplacedByTokenId);
    }

    // Verifies that rotation atomically revokes the old token and inserts the replacement.
    protected async Task RefreshTokenRotationRevokesExistingTokenAndInsertsReplacementCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        Guid familyId = Guid.NewGuid();
        RefreshTokenRecord existing = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "existing");
        RefreshTokenRecord replacement = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "replacement");
        DateTimeOffset rotationUtc = Now.AddMinutes(5);
        await _store.CreateRefreshTokenAsync(existing);

        TokenRotationResult result = await _store.RotateRefreshTokenAsync(
            existing.TokenHash,
            replacement,
            rotationUtc,
            MaximumActiveRefreshTokensPerFamily);

        Assert.Equal(TokenRotationStatus.Success, result.Status);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(familyId, result.FamilyId);

        RefreshTokenRecord? rotatedExisting = await _store.FindRefreshTokenByHashAsync(existing.TokenHash);
        Assert.NotNull(rotatedExisting);
        Assert.Equal(rotationUtc, rotatedExisting.RevokedUtc);
        Assert.Equal(replacement.Id, rotatedExisting.ReplacedByTokenId);

        RefreshTokenRecord? insertedReplacement = await _store.FindRefreshTokenByHashAsync(replacement.TokenHash);
        Assert.NotNull(insertedReplacement);
        Assert.Equal(replacement.Id, insertedReplacement.Id);
        Assert.Equal(user.Id, insertedReplacement.UserId);
        Assert.Equal(familyId, insertedReplacement.FamilyId);
        Assert.Null(insertedReplacement.RevokedUtc);
        Assert.Null(insertedReplacement.ReplacedByTokenId);
    }

    // Verifies that replaying a revoked refresh token revokes the active token family.
    protected async Task ReusedRefreshTokenRevokesActiveFamilyMembersCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        Guid familyId = Guid.NewGuid();
        RefreshTokenRecord existing = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "existing");
        RefreshTokenRecord firstReplacement = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "replacement-one");
        RefreshTokenRecord secondReplacement = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "replacement-two");
        DateTimeOffset firstRotationUtc = Now.AddMinutes(5);
        DateTimeOffset reuseUtc = Now.AddMinutes(6);
        await _store.CreateRefreshTokenAsync(existing);
        TokenRotationResult firstResult = await _store.RotateRefreshTokenAsync(
            existing.TokenHash,
            firstReplacement,
            firstRotationUtc,
            MaximumActiveRefreshTokensPerFamily);
        Assert.Equal(TokenRotationStatus.Success, firstResult.Status);

        TokenRotationResult reuseResult = await _store.RotateRefreshTokenAsync(
            existing.TokenHash,
            secondReplacement,
            reuseUtc,
            MaximumActiveRefreshTokensPerFamily);

        Assert.Equal(TokenRotationStatus.Reused, reuseResult.Status);
        Assert.Equal(user.Id, reuseResult.UserId);
        Assert.Equal(familyId, reuseResult.FamilyId);

        RefreshTokenRecord? revokedReplacement = await _store.FindRefreshTokenByHashAsync(firstReplacement.TokenHash);
        Assert.NotNull(revokedReplacement);
        Assert.Equal(reuseUtc, revokedReplacement.RevokedUtc);

        RefreshTokenRecord? notInsertedReplacement = await _store.FindRefreshTokenByHashAsync(secondReplacement.TokenHash);
        Assert.Null(notInsertedReplacement);
    }

    // Verifies that expired refresh tokens are revoked and reported as expired instead of rotated.
    protected async Task ExpiredRefreshTokenRotationReturnsExpiredAndDoesNotInsertReplacementCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        Guid familyId = Guid.NewGuid();
        RefreshTokenRecord expired = CreateRefreshToken(
            user,
            familyId: familyId,
            tokenHashPrefix: "expired",
            expiresUtc: Now.AddMinutes(-1));
        RefreshTokenRecord replacement = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "replacement");
        await _store.CreateRefreshTokenAsync(expired);

        TokenRotationResult result = await _store.RotateRefreshTokenAsync(
            expired.TokenHash,
            replacement,
            Now,
            MaximumActiveRefreshTokensPerFamily);

        Assert.Equal(TokenRotationStatus.Expired, result.Status);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(familyId, result.FamilyId);

        RefreshTokenRecord? revokedExpired = await _store.FindRefreshTokenByHashAsync(expired.TokenHash);
        Assert.NotNull(revokedExpired);
        Assert.Equal(Now, revokedExpired.RevokedUtc);
        Assert.Null(revokedExpired.ReplacedByTokenId);

        RefreshTokenRecord? notInsertedReplacement = await _store.FindRefreshTokenByHashAsync(replacement.TokenHash);
        Assert.Null(notInsertedReplacement);
    }

    // Verifies that invalid persisted user state revokes the family and prevents replacement insertion.
    protected async Task InvalidUserRefreshTokenRotationRevokesFamilyAndReturnsUserInvalidCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: false);
        Guid familyId = Guid.NewGuid();
        RefreshTokenRecord existing = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "existing");
        RefreshTokenRecord sibling = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "sibling");
        RefreshTokenRecord replacement = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "replacement");
        DateTimeOffset rotationUtc = Now.AddMinutes(5);
        await _store.CreateRefreshTokenAsync(existing);
        await _store.CreateRefreshTokenAsync(sibling);

        TokenRotationResult result = await _store.RotateRefreshTokenAsync(
            existing.TokenHash,
            replacement,
            rotationUtc,
            MaximumActiveRefreshTokensPerFamily);

        Assert.Equal(TokenRotationStatus.UserInvalid, result.Status);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(familyId, result.FamilyId);

        RefreshTokenRecord? revokedExisting = await _store.FindRefreshTokenByHashAsync(existing.TokenHash);
        Assert.NotNull(revokedExisting);
        Assert.Equal(rotationUtc, revokedExisting.RevokedUtc);

        RefreshTokenRecord? revokedSibling = await _store.FindRefreshTokenByHashAsync(sibling.TokenHash);
        Assert.NotNull(revokedSibling);
        Assert.Equal(rotationUtc, revokedSibling.RevokedUtc);

        RefreshTokenRecord? notInsertedReplacement = await _store.FindRefreshTokenByHashAsync(replacement.TokenHash);
        Assert.Null(notInsertedReplacement);
    }

    // Verifies that explicit family revocation only affects active tokens in that family.
    protected async Task ExplicitRefreshTokenFamilyRevocationRevokesActiveFamilyTokensCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        Guid revokedFamilyId = Guid.NewGuid();
        Guid untouchedFamilyId = Guid.NewGuid();
        RefreshTokenRecord first = CreateRefreshToken(user, familyId: revokedFamilyId, tokenHashPrefix: "first");
        RefreshTokenRecord second = CreateRefreshToken(user, familyId: revokedFamilyId, tokenHashPrefix: "second");
        RefreshTokenRecord untouched = CreateRefreshToken(user, familyId: untouchedFamilyId, tokenHashPrefix: "untouched");
        DateTimeOffset revokedUtc = Now.AddMinutes(5);
        await _store.CreateRefreshTokenAsync(first);
        await _store.CreateRefreshTokenAsync(second);
        await _store.CreateRefreshTokenAsync(untouched);

        int revoked = await _store.RevokeRefreshTokenFamilyAsync(revokedFamilyId, revokedUtc);
        int revokedAgain = await _store.RevokeRefreshTokenFamilyAsync(revokedFamilyId, revokedUtc.AddMinutes(1));

        Assert.Equal(2, revoked);
        Assert.Equal(0, revokedAgain);

        RefreshTokenRecord? revokedFirst = await _store.FindRefreshTokenByHashAsync(first.TokenHash);
        Assert.NotNull(revokedFirst);
        Assert.Equal(revokedUtc, revokedFirst.RevokedUtc);

        RefreshTokenRecord? revokedSecond = await _store.FindRefreshTokenByHashAsync(second.TokenHash);
        Assert.NotNull(revokedSecond);
        Assert.Equal(revokedUtc, revokedSecond.RevokedUtc);

        RefreshTokenRecord? activeUntouched = await _store.FindRefreshTokenByHashAsync(untouched.TokenHash);
        Assert.NotNull(activeUntouched);
        Assert.Null(activeUntouched.RevokedUtc);
    }

    // Verifies that a general one-time token can be consumed exactly once.
    protected async Task GeneralOneTimeTokenCanBeConsumedOnlyOnceCore()
    {
        AuthUser user = await CreateUserAsync();
        string purpose = "oauth_exchange:google";
        string tokenHash = $"exchange-{Guid.NewGuid():N}";
        DateTimeOffset expiresUtc = Now.AddMinutes(10);

        bool created = await _store.CreateOneTimeTokenAsync(user.Id, purpose, tokenHash, Now, expiresUtc);
        Assert.True(created);

        OneTimeTokenRecord? first = await _store.ConsumeOneTimeTokenAsync(purpose, tokenHash, Now.AddMinutes(1));
        Assert.NotNull(first);
        Assert.Equal(user.Id, first.UserId);
        Assert.Equal(purpose, first.Purpose);
        Assert.Equal(expiresUtc, first.ExpiresUtc);

        OneTimeTokenRecord? replay = await _store.ConsumeOneTimeTokenAsync(purpose, tokenHash, Now.AddMinutes(2));
        Assert.Null(replay);
    }

    // Verifies that expired general one-time tokens are not consumed.
    protected async Task ExpiredGeneralOneTimeTokenIsNotConsumedCore()
    {
        AuthUser user = await CreateUserAsync();
        string purpose = "oauth_exchange:google";
        string tokenHash = $"expired-{Guid.NewGuid():N}";

        bool created = await _store.CreateOneTimeTokenAsync(
            user.Id,
            purpose,
            tokenHash,
            Now.AddMinutes(-10),
            Now.AddMinutes(-1));
        Assert.True(created);

        OneTimeTokenRecord? consumed = await _store.ConsumeOneTimeTokenAsync(purpose, tokenHash, Now);
        Assert.Null(consumed);
    }

    // Verifies that replacing an email verification token invalidates the previous active token.
    protected async Task ReplaceVerificationTokenConsumesPreviousActiveTokenCore()
    {
        AuthUser user = await CreateUserAsync(verificationTokenHash: "initial-verification");
        string replacementHash = $"replacement-{Guid.NewGuid():N}";

        bool replaced = await _store.ReplaceOneTimeTokenAsync(
            user.Id,
            "email_verification",
            replacementHash,
            Now.AddMinutes(1),
            Now.AddHours(2));
        Assert.True(replaced);

        Guid? oldResult = await _store.VerifyEmailAsync("initial-verification", Now.AddMinutes(2));
        Assert.Null(oldResult);

        Guid? newResult = await _store.VerifyEmailAsync(replacementHash, Now.AddMinutes(3));
        Assert.Equal(user.Id, newResult);

        Guid? replay = await _store.VerifyEmailAsync(replacementHash, Now.AddMinutes(4));
        Assert.Null(replay);
    }

    // Verifies that unsupported one-time token purposes fail explicitly instead of choosing an arbitrary table.
    protected async Task UnsupportedOneTimeTokenPurposeFailsExplicitlyCore()
    {
        AuthUser user = await CreateUserAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _store.CreateOneTimeTokenAsync(
                user.Id,
                "unsupported",
                $"unsupported-{Guid.NewGuid():N}",
                Now,
                Now.AddMinutes(5)));
    }

    // Verifies that a failed verification-token insert rolls back the preceding user insert.
    protected async Task RegistrationRollsBackWhenVerificationTokenInsertFailsCore()
    {
        await InitializeStoreAsync();
        const string duplicateTokenHash = "fault-duplicate-verification-token";
        AuthUser first = CreateUserRecord("first-fault", emailVerified: false);
        AuthUser second = CreateUserRecord("second-fault", emailVerified: false);
        Assert.True(await _store.CreateUserWithVerificationTokenAsync(first, duplicateTokenHash, Now.AddHours(1)));

        bool created = await _store.CreateUserWithVerificationTokenAsync(second, duplicateTokenHash, Now.AddHours(1));

        Assert.False(created);
        Assert.Null(await _store.FindUserByNormalizedEmailAsync(second.NormalizedEmail));
    }

    // Verifies that concurrent creation attempts for one normalized email converge on one account.
    protected async Task ConcurrentRegistrationWithSameNormalizedEmailCreatesExactlyOneCore()
    {
        await InitializeStoreAsync();
        string normalizedEmail = $"CONCURRENT-{Guid.NewGuid():N}@EXAMPLE.COM";
        AuthUser CreateUser() => new(
            Guid.NewGuid(),
            normalizedEmail.ToLowerInvariant(),
            normalizedEmail,
            "hash",
            EmailVerifiedUtc: null,
            IsActive: true,
            FailedLoginAttempts: 0,
            LockoutEndUtc: null,
            SecurityVersion: 1,
            CreatedUtc: Now,
            UpdatedUtc: Now);

        Task<bool>[] attempts = Enumerable.Range(0, 2)
            .Select(index =>
            {
                AuthUser user = CreateUser();
                return _store.CreateUserWithVerificationTokenAsync(user, $"verification-{index}-{Guid.NewGuid():N}", Now.AddHours(1));
            })
            .ToArray();

        bool[] results = await Task.WhenAll(attempts);
        Assert.Single(results, static created => created);
        Assert.NotNull(await _store.FindUserByNormalizedEmailAsync(normalizedEmail));
    }

    // Verifies atomic failed-login increments and lockout-threshold crossing under concurrency.
    protected async Task ConcurrentLoginFailuresReachLockoutThresholdCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        DateTimeOffset lockoutEndUtc = Now.AddMinutes(10);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            _store.RecordLoginFailureAsync(user.Id, 3, lockoutEndUtc, Now)));

        AuthUser? updated = await _store.FindUserByIdAsync(user.Id);
        Assert.NotNull(updated);
        Assert.Equal(3, updated.FailedLoginAttempts);
        Assert.Equal(lockoutEndUtc, updated.LockoutEndUtc);
    }

    // Verifies that concurrent password-reset consumers produce one password change.
    protected async Task ConcurrentPasswordResetsSucceedExactlyOnceCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        string tokenHash = $"password-reset-{Guid.NewGuid():N}";
        Assert.True(await _store.ReplaceOneTimeTokenAsync(user.Id, "password_reset", tokenHash, Now, Now.AddMinutes(10)));

        Guid?[] results = await Task.WhenAll(
            _store.ResetPasswordAsync(tokenHash, "replacement-one", Now.AddMinutes(1)),
            _store.ResetPasswordAsync(tokenHash, "replacement-two", Now.AddMinutes(1)));

        Assert.Single(results, static result => result.HasValue);
        AuthUser? updated = await _store.FindUserByIdAsync(user.Id);
        Assert.NotNull(updated);
        Assert.Equal(2, updated.SecurityVersion);
        Assert.Contains(updated.PasswordHash!, ExpectedReplacementPasswordHashes);
    }

    // Verifies that concurrent verification-token replacements leave one consumable token.
    protected async Task ConcurrentEmailTokenReplacementLeavesOneActiveTokenCore()
    {
        AuthUser user = await CreateUserAsync();
        string first = $"verification-first-{Guid.NewGuid():N}";
        string second = $"verification-second-{Guid.NewGuid():N}";
        bool[] replaced = await Task.WhenAll(
            _store.ReplaceOneTimeTokenAsync(user.Id, "email_verification", first, Now.AddMinutes(1), Now.AddHours(1)),
            _store.ReplaceOneTimeTokenAsync(user.Id, "email_verification", second, Now.AddMinutes(1), Now.AddHours(1)));
        Assert.Contains(true, replaced);

        Guid? firstResult = await _store.VerifyEmailAsync(first, Now.AddMinutes(2));
        Guid? secondResult = await _store.VerifyEmailAsync(second, Now.AddMinutes(2));
        Assert.Single(new[] { firstResult, secondResult }, static result => result.HasValue);
    }

    // Verifies that concurrent one-time-token consumers produce exactly one winner.
    protected async Task ConcurrentOneTimeTokenConsumptionSucceedsExactlyOnceCore()
    {
        AuthUser user = await CreateUserAsync();
        string purpose = "oauth_exchange:google";
        string tokenHash = $"concurrent-{Guid.NewGuid():N}";
        Assert.True(await _store.CreateOneTimeTokenAsync(user.Id, purpose, tokenHash, Now, Now.AddMinutes(10)));

        OneTimeTokenRecord?[] results = await Task.WhenAll(
            _store.ConsumeOneTimeTokenAsync(purpose, tokenHash, Now.AddMinutes(1)),
            _store.ConsumeOneTimeTokenAsync(purpose, tokenHash, Now.AddMinutes(1)));
        Assert.Single(results, static result => result is not null);
    }

    // Verifies that concurrent refresh rotation detects replay and leaves no active competing replacement.
    protected async Task ConcurrentRefreshRotationDetectsReplayCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        Guid familyId = Guid.NewGuid();
        RefreshTokenRecord existing = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "concurrent-existing");
        RefreshTokenRecord first = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "concurrent-first");
        RefreshTokenRecord second = CreateRefreshToken(user, familyId: familyId, tokenHashPrefix: "concurrent-second");
        await _store.CreateRefreshTokenAsync(existing);

        TokenRotationResult[] results = await Task.WhenAll(
            _store.RotateRefreshTokenAsync(
                existing.TokenHash,
                first,
                Now.AddMinutes(1),
                MaximumActiveRefreshTokensPerFamily),
            _store.RotateRefreshTokenAsync(
                existing.TokenHash,
                second,
                Now.AddMinutes(1),
                MaximumActiveRefreshTokensPerFamily));

        Assert.Single(results, static result => result.Status == TokenRotationStatus.Success);
        Assert.Single(results, static result => result.Status == TokenRotationStatus.Reused);
        foreach (RefreshTokenRecord replacement in new[] { first, second })
        {
            RefreshTokenRecord? stored = await _store.FindRefreshTokenByHashAsync(replacement.TokenHash);
            Assert.True(stored is null || stored.RevokedUtc.HasValue);
        }
    }

    // Verifies that concurrent global-role assignment and removal preserve uniqueness.
    protected async Task ConcurrentGlobalRoleChangesRemainAtomicCore()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        string name = $"Concurrent Role {Guid.NewGuid():N}";
        RoleRecord? role = await _store.CreateRoleAsync(name, name.ToUpperInvariant(), "Concurrent provider contract role.", Now);
        Assert.NotNull(role);

        bool[] assigned = await Task.WhenAll(
            _store.AssignGlobalRoleToUserAsync(user.Id, role.Id, Now),
            _store.AssignGlobalRoleToUserAsync(user.Id, role.Id, Now));
        Assert.Single(assigned, static result => result);

        bool[] removed = await Task.WhenAll(
            _store.RemoveGlobalRoleFromUserAsync(user.Id, role.Id, Now.AddMinutes(1)),
            _store.RemoveGlobalRoleFromUserAsync(user.Id, role.Id, Now.AddMinutes(1)));
        Assert.Single(removed, static result => result);
    }

    // Verifies that concurrent tenant-ownership transfers elect one new owner.
    protected async Task ConcurrentTenantOwnershipTransferElectsOneOwnerCore()
    {
        AuthUser owner = await CreateUserAsync(emailVerified: true);
        AuthUser firstMember = await CreateUserAsync(emailVerified: true);
        AuthUser secondMember = await CreateUserAsync(emailVerified: true);
        TenantRecord? tenant = await _store.CreateTenantAsync($"Tenant {Guid.NewGuid():N}", $"tenant-{Guid.NewGuid():N}", owner.Id, Now);
        Assert.NotNull(tenant);
        Assert.True(await _store.AddTenantMemberAsync(tenant.Id, firstMember.Id, Now));
        Assert.True(await _store.AddTenantMemberAsync(tenant.Id, secondMember.Id, Now));

        TenantOwnershipTransferResult[] results = await Task.WhenAll(
            _store.TransferTenantOwnershipAsync(tenant.Id, owner.Id, firstMember.Id, Now.AddMinutes(1)),
            _store.TransferTenantOwnershipAsync(tenant.Id, owner.Id, secondMember.Id, Now.AddMinutes(1)));

        Assert.Single(results, static result => result.Status == TenantOwnershipTransferStatus.Success);
        TenantOwnerRecord? persisted = await _store.GetTenantOwnerAsync(tenant.Id);
        Assert.NotNull(persisted);
        Assert.Contains(persisted.UserId, new[] { firstMember.Id, secondMember.Id });
    }

    // Verifies bounded keyset traversal, equal timestamps, concurrent inserts, membership chronology, and cancellation.
    protected async Task BoundedPaginationIsStableAndCompleteCore()
    {
        await InitializeStoreAsync();
        AuthUser[] originalUsers =
        [
            await CreateUserAsync(emailVerified: true),
            await CreateUserAsync(emailVerified: true),
            await CreateUserAsync(emailVerified: true),
            await CreateUserAsync(emailVerified: true)
        ];
        Guid[] baselineUserIds = (await _store.ListUsersAsync(new AuthPageQuery(200, null))).Items
            .Select(static user => user.Id)
            .ToArray();
        AuthPageSlice<AuthUser> firstUsers = await _store.ListUsersAsync(new AuthPageQuery(2, null));
        Assert.Equal(2, firstUsers.Items.Count);
        Assert.NotNull(firstUsers.Next);
        AuthUser insertedAfterFirstPage = await CreateUserAsync(
            emailVerified: true,
            createdUtc: Now.AddMinutes(1));
        AuthPageSlice<AuthUser> secondUsers = await _store.ListUsersAsync(
            new AuthPageQuery(2, firstUsers.Next));
        Guid[] traversedUserIds = firstUsers.Items.Concat(secondUsers.Items)
            .Select(static user => user.Id)
            .ToArray();
        Assert.Equal(baselineUserIds, traversedUserIds);
        Assert.Equal(originalUsers.Select(static user => user.Id).Order(), traversedUserIds.Order());
        Assert.DoesNotContain(insertedAfterFirstPage.Id, traversedUserIds);
        Assert.Equal(traversedUserIds.Length, traversedUserIds.Distinct().Count());
        Assert.Null(secondUsers.Next);

        AuthPageSlice<AuthUser> emptyUsers = await _store.ListUsersAsync(
            new AuthPageQuery(2, new AuthPageBoundary(DateTimeOffset.MinValue, Guid.Empty)));
        Assert.Empty(emptyUsers.Items);
        Assert.Null(emptyUsers.Next);

        IReadOnlyList<RoleRecord> roles = await DrainPagesAsync(
            query => _store.ListRolesAsync(query),
            pageSize: 1);
        IReadOnlyList<PermissionRecord> permissions = await DrainPagesAsync(
            query => _store.ListPermissionsAsync(query),
            pageSize: 2);
        Assert.NotEmpty(roles);
        Assert.NotEmpty(permissions);
        Assert.Equal(roles.Count, roles.Select(static role => role.Id).Distinct().Count());
        Assert.Equal(permissions.Count, permissions.Select(static permission => permission.Id).Distinct().Count());

        AuditRecord[] audits =
        [
            new(Guid.NewGuid(), Now, "pagination.one", null, null, null, null, null),
            new(Guid.NewGuid(), Now, "pagination.two", null, null, null, null, null),
            new(Guid.NewGuid(), Now, "pagination.three", null, null, null, null, null)
        ];
        foreach (AuditRecord audit in audits)
        {
            await _store.WriteAuditAsync(audit);
        }
        Guid[] baselineAuditIds = (await _store.ListAuditAsync(new AuthPageQuery(200, null))).Items
            .Select(static audit => audit.Id)
            .ToArray();
        IReadOnlyList<AuditRecord> traversedAudits = await DrainPagesAsync(
            query => _store.ListAuditAsync(query),
            pageSize: 1);
        Assert.Equal(baselineAuditIds, traversedAudits.Select(static audit => audit.Id));

        AuthUser firstOwner = await CreateUserAsync(emailVerified: true);
        AuthUser secondOwner = await CreateUserAsync(emailVerified: true);
        AuthUser tenantViewer = await CreateUserAsync(emailVerified: true);
        TenantRecord newerTenant = (await _store.CreateTenantAsync(
            $"Newer tenant {Guid.NewGuid():N}",
            $"newer-{Guid.NewGuid():N}",
            firstOwner.Id,
            Now.AddHours(2)))!;
        TenantRecord olderTenant = (await _store.CreateTenantAsync(
            $"Older tenant {Guid.NewGuid():N}",
            $"older-{Guid.NewGuid():N}",
            secondOwner.Id,
            Now.AddHours(-2)))!;
        Assert.True(await _store.AddTenantMemberAsync(newerTenant.Id, tenantViewer.Id, Now.AddMinutes(1)));
        Assert.True(await _store.AddTenantMemberAsync(olderTenant.Id, tenantViewer.Id, Now.AddMinutes(2)));
        IReadOnlyList<TenantRecord> viewerTenants = await DrainPagesAsync(
            query => _store.ListTenantsForUserAsync(tenantViewer.Id, query),
            pageSize: 1);
        Assert.Equal([olderTenant.Id, newerTenant.Id], viewerTenants.Select(static tenant => tenant.Id));

        AuthUser memberOwner = await CreateUserAsync(emailVerified: true);
        AuthUser multiRoleMember = await CreateUserAsync(emailVerified: true);
        AuthUser lookaheadMember = await CreateUserAsync(emailVerified: true);
        TenantRecord memberTenant = (await _store.CreateTenantAsync(
            $"Member tenant {Guid.NewGuid():N}",
            $"members-{Guid.NewGuid():N}",
            memberOwner.Id,
            Now))!;
        Assert.True(await _store.AddTenantMemberAsync(memberTenant.Id, multiRoleMember.Id, Now.AddMinutes(2)));
        Assert.True(await _store.AddTenantMemberAsync(memberTenant.Id, lookaheadMember.Id, Now.AddMinutes(1)));
        Assert.True(await _store.AssignTenantRoleToUserAsync(
            memberTenant.Id,
            multiRoleMember.Id,
            Guid.Parse("40000000-0000-0000-0000-000000000002"),
            Now.AddMinutes(3)));
        AuthPageSlice<TenantMemberRecord> firstMembers = await _store.ListTenantMembersAsync(
            memberTenant.Id,
            new AuthPageQuery(1, null));
        Assert.Single(firstMembers.Items);
        Assert.Equal(multiRoleMember.Id, firstMembers.Items[0].UserId);
        Assert.Contains(TenantAuthRoles.Member, firstMembers.Items[0].Roles);
        Assert.Contains(TenantAuthRoles.Manager, firstMembers.Items[0].Roles);
        Assert.NotNull(firstMembers.Next);
        IReadOnlyList<TenantMemberRecord> remainingMembers = await DrainPagesAsync(
            query => _store.ListTenantMembersAsync(memberTenant.Id, query),
            pageSize: 1,
            firstMembers.Next);
        Guid[] allMemberIds = firstMembers.Items.Concat(remainingMembers)
            .Select(static member => member.UserId)
            .ToArray();
        Assert.Equal(3, allMemberIds.Length);
        Assert.Equal(3, allMemberIds.Distinct().Count());
        Assert.DoesNotContain(multiRoleMember.Id, remainingMembers.Select(static member => member.UserId));

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _store.ListUsersAsync(new AuthPageQuery(10, null), cancellation.Token));
    }

    // Creates a provider-specific store instance without leaking provider implementation types into the base tests.
    protected abstract Task<object> CreateProviderStoreAsync();

    // Cleans up provider-specific resources created for the current test class instance.
    protected virtual Task DisposeProviderResourcesAsync() => Task.CompletedTask;

    // Initializes the provider store through the shared auth-store contract.
    protected Task InitializeStoreAsync() => _store.InitializeAsync();

    // Creates one unsaved user record for rollback and concurrency contracts.
    private static AuthUser CreateUserRecord(string prefix, bool emailVerified)
    {
        Guid id = Guid.NewGuid();
        return new AuthUser(
            id,
            $"{prefix}-{id:N}@example.com",
            $"{prefix}-{id:N}@example.com".ToUpperInvariant(),
            "hash",
            EmailVerifiedUtc: emailVerified ? Now : null,
            IsActive: true,
            FailedLoginAttempts: 0,
            LockoutEndUtc: null,
            SecurityVersion: 1,
            CreatedUtc: Now,
            UpdatedUtc: Now);
    }

    // Creates one user for provider-contract tests.
    private async Task<AuthUser> CreateUserAsync(
        bool emailVerified = false,
        string? verificationTokenHash = null,
        DateTimeOffset? createdUtc = null)
    {
        await InitializeStoreAsync();

        Guid id = Guid.NewGuid();
        DateTimeOffset persistedUtc = createdUtc ?? Now;
        AuthUser user = new(
            id,
            $"person-{id:N}@example.com",
            $"PERSON-{id:N}@EXAMPLE.COM",
            "hash",
            EmailVerifiedUtc: emailVerified ? Now : null,
            IsActive: true,
            FailedLoginAttempts: 0,
            LockoutEndUtc: null,
            SecurityVersion: 1,
            CreatedUtc: persistedUtc,
            UpdatedUtc: persistedUtc);

        bool created = await _store.CreateUserWithVerificationTokenAsync(
            user,
            verificationTokenHash ?? $"verification-{Guid.NewGuid():N}",
            Now.AddHours(1));
        Assert.True(created);
        return user;
    }

    // Drains one provider keyset query and fails if a cursor cycle prevents termination.
    private static async Task<IReadOnlyList<T>> DrainPagesAsync<T>(
        Func<AuthPageQuery, Task<AuthPageSlice<T>>> query,
        int pageSize,
        AuthPageBoundary? after = null)
    {
        List<T> items = [];
        for (int pageNumber = 0; pageNumber < 1_000; pageNumber++)
        {
            AuthPageSlice<T> page = await query(new AuthPageQuery(pageSize, after));
            items.AddRange(page.Items);
            if (page.Next is null)
            {
                return items;
            }

            after = page.Next;
        }

        throw new InvalidOperationException("Provider pagination did not terminate within the safety bound.");
    }

    // Creates one refresh-token record with deterministic user and family metadata.
    private static RefreshTokenRecord CreateRefreshToken(
        AuthUser user,
        Guid? familyId = null,
        string tokenHashPrefix = "refresh",
        DateTimeOffset? expiresUtc = null)
    {
        DateTimeOffset effectiveExpiresUtc = expiresUtc ?? Now.AddDays(30);
        return new RefreshTokenRecord(
            Guid.NewGuid(),
            user.Id,
            $"{tokenHashPrefix}-{Guid.NewGuid():N}",
            familyId ?? Guid.NewGuid(),
            user.SecurityVersion,
            "127.0.0.1",
            "provider-contract-test",
            Now,
            Now,
            effectiveExpiresUtc,
            RevokedUtc: null,
            ReplacedByTokenId: null);
    }

    // Casts a provider-specific object to the shared internal store contract.
    private static IAuthStore RequireAuthStore(object store) =>
        store as IAuthStore ?? throw new InvalidOperationException("The provider test fixture did not create an IAuthStore instance.");
}
