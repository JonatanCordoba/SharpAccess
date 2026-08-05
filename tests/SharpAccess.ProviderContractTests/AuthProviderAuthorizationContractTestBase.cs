using SharpAccess;
using SharpAccess.Domain;
using SharpAccess.Persistence;

namespace SharpAccess.ProviderContractTests;

public abstract class AuthProviderAuthorizationContractTestBase : IAsyncLifetime
{
    private static readonly Guid GlobalAdminRoleId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantOwnerRoleId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private IAuthStore _store = null!;

    protected static DateTimeOffset Now { get; } = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    // Creates one provider-specific store instance for the concrete contract test class.
    public Task InitializeAsync()
    {
        _store = RequireAuthStore(CreateProviderStore());
        return Task.CompletedTask;
    }

    // Disposes provider-specific resources created by the concrete contract test class.
    public Task DisposeAsync() => DisposeProviderResourcesAsync();

    // Verifies that users can be found through provider-neutral lookup paths.
    [Fact]
    public async Task UserCanBeFoundByNormalizedEmailAndId()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);

        AuthUser? byEmail = await _store.FindUserByNormalizedEmailAsync(user.NormalizedEmail);
        AuthUser? byId = await _store.FindUserByIdAsync(user.Id);

        Assert.NotNull(byEmail);
        Assert.NotNull(byId);
        Assert.Equal(user.Id, byEmail.Id);
        Assert.Equal(user.Id, byId.Id);
        Assert.Equal(user.Email, byEmail.Email);
        Assert.Equal(user.NormalizedEmail, byId.NormalizedEmail);
    }

    // Verifies that dynamic global role changes affect only the global authorization catalog.
    [Fact]
    public async Task GlobalRolePermissionAssignmentUpdatesOnlyGlobalAuthorization()
    {
        AuthUser user = await CreateUserAsync(emailVerified: true);
        string roleName = $"Contract Auditor {Guid.NewGuid():N}";
        RoleRecord? role = await _store.CreateRoleAsync(
            roleName,
            roleName.ToUpperInvariant(),
            "Reads audit data.",
            Now.AddMinutes(1));
        Assert.NotNull(role);
        PermissionRecord permission = (await _store.ListPermissionsAsync(new AuthPageQuery(200, null))).Items
            .Single(static item => item.Name == AuthPermissions.AuditRead);

        bool permissionAssigned = await _store.AssignPermissionToRoleAsync(
            role.Id,
            permission.Id,
            Now.AddMinutes(2));
        bool roleAssigned = await _store.AssignGlobalRoleToUserAsync(
            user.Id,
            role.Id,
            Now.AddMinutes(3));
        EffectiveAuthorizationContext context = await _store.GetEffectiveAuthorizationContextAsync(
            user.Id,
            tenantId: null);

        Assert.True(permissionAssigned);
        Assert.True(roleAssigned);
        Assert.Contains(roleName, context.Global.Roles);
        Assert.Contains(AuthPermissions.AuditRead, context.Global.Permissions);
        Assert.Null(context.Tenant);
        Assert.DoesNotContain(TenantAuthPermissions.TenantRead, context.Global.Permissions);
    }

    // Verifies tenant ownership, membership, and role projection without global-role leakage.
    [Fact]
    public async Task TenantMembershipAndOwnershipArePersistedSeparately()
    {
        AuthUser owner = await CreateUserAsync(emailVerified: true);
        AuthUser member = await CreateUserAsync(emailVerified: true);
        TenantRecord? tenant = await _store.CreateTenantAsync(
            "Contract Tenant",
            $"contract-{Guid.NewGuid():N}",
            owner.Id,
            Now.AddMinutes(1));
        Assert.NotNull(tenant);

        bool memberAdded = await _store.AddTenantMemberAsync(
            tenant.Id,
            member.Id,
            Now.AddMinutes(2));
        TenantOwnerRecord? persistedOwner = await _store.GetTenantOwnerAsync(tenant.Id);
        IReadOnlyList<TenantMemberRecord> members = (await _store.ListTenantMembersAsync(tenant.Id, new AuthPageQuery(200, null))).Items;
        EffectiveAuthorizationContext ownerContext = await _store.GetEffectiveAuthorizationContextAsync(owner.Id, tenant.Id);
        EffectiveAuthorizationContext memberContext = await _store.GetEffectiveAuthorizationContextAsync(member.Id, tenant.Id);

        Assert.True(memberAdded);
        Assert.Equal(owner.Id, persistedOwner?.UserId);
        Assert.Contains(members, item =>
            item.UserId == owner.Id
            && item.IsOwner
            && item.Roles.Contains(TenantAuthRoles.Owner));
        Assert.Contains(members, item =>
            item.UserId == member.Id
            && !item.IsOwner
            && item.Roles.Contains(TenantAuthRoles.Member));
        Assert.True(ownerContext.Tenant?.IsOwner);
        Assert.Contains(TenantAuthRoles.Owner, ownerContext.Tenant!.Roles);
        Assert.Contains(TenantAuthPermissions.OwnershipTransfer, ownerContext.Tenant.Permissions);
        Assert.Contains(TenantAuthRoles.Member, memberContext.Tenant!.Roles);
        Assert.Contains(TenantAuthPermissions.TenantRead, memberContext.Tenant.Permissions);
        Assert.DoesNotContain(AuthRoles.Admin, memberContext.Tenant.Roles);
        Assert.DoesNotContain(TenantAuthPermissions.TenantRead, memberContext.Global.Permissions);
    }

    // Verifies that a global role identifier cannot be assigned through the tenant role path.
    [Fact]
    public async Task GlobalRoleCannotBeAssignedAsTenantRole()
    {
        AuthUser owner = await CreateUserAsync(emailVerified: true);
        AuthUser member = await CreateUserAsync(emailVerified: true);
        TenantRecord tenant = (await _store.CreateTenantAsync(
            "Boundary Tenant",
            $"boundary-{Guid.NewGuid():N}",
            owner.Id,
            Now.AddMinutes(1)))!;
        Assert.True(await _store.AddTenantMemberAsync(tenant.Id, member.Id, Now.AddMinutes(2)));

        bool assigned = await _store.AssignTenantRoleToUserAsync(
            tenant.Id,
            member.Id,
            GlobalAdminRoleId,
            Now.AddMinutes(3));
        EffectiveAuthorizationContext context = await _store.GetEffectiveAuthorizationContextAsync(member.Id, tenant.Id);

        Assert.False(assigned);
        Assert.DoesNotContain(AuthRoles.Admin, context.Tenant!.Roles);
        Assert.DoesNotContain(AuthPermissions.UsersManage, context.Tenant.Permissions);
    }

    // Verifies that the immutable Owner role cannot be assigned or removed through ordinary role methods.
    [Fact]
    public async Task OwnerRoleChangesRequireOwnershipTransfer()
    {
        AuthUser owner = await CreateUserAsync(emailVerified: true);
        AuthUser member = await CreateUserAsync(emailVerified: true);
        TenantRecord tenant = (await _store.CreateTenantAsync(
            "Owner Guard Tenant",
            $"owner-guard-{Guid.NewGuid():N}",
            owner.Id,
            Now.AddMinutes(1)))!;
        Assert.True(await _store.AddTenantMemberAsync(tenant.Id, member.Id, Now.AddMinutes(2)));

        bool assignedDirectly = await _store.AssignTenantRoleToUserAsync(
            tenant.Id,
            member.Id,
            TenantOwnerRoleId,
            Now.AddMinutes(3));
        bool removedDirectly = await _store.RemoveTenantRoleFromUserAsync(
            tenant.Id,
            owner.Id,
            TenantOwnerRoleId,
            Now.AddMinutes(4));
        EffectiveAuthorizationContext ownerContext = await _store.GetEffectiveAuthorizationContextAsync(owner.Id, tenant.Id);
        EffectiveAuthorizationContext memberContext = await _store.GetEffectiveAuthorizationContextAsync(member.Id, tenant.Id);

        Assert.False(assignedDirectly);
        Assert.False(removedDirectly);
        Assert.True(ownerContext.Tenant!.IsOwner);
        Assert.Contains(TenantAuthRoles.Owner, ownerContext.Tenant.Roles);
        Assert.False(memberContext.Tenant!.IsOwner);
        Assert.DoesNotContain(TenantAuthRoles.Owner, memberContext.Tenant.Roles);
    }

    // Verifies that tenant role assignments remain bound to the selected tenant.
    [Fact]
    public async Task TenantRoleDoesNotCrossTenantBoundary()
    {
        AuthUser firstOwner = await CreateUserAsync(emailVerified: true);
        AuthUser secondOwner = await CreateUserAsync(emailVerified: true);
        AuthUser member = await CreateUserAsync(emailVerified: true);
        TenantRecord first = (await _store.CreateTenantAsync(
            "First Tenant",
            $"first-{Guid.NewGuid():N}",
            firstOwner.Id,
            Now.AddMinutes(1)))!;
        TenantRecord second = (await _store.CreateTenantAsync(
            "Second Tenant",
            $"second-{Guid.NewGuid():N}",
            secondOwner.Id,
            Now.AddMinutes(2)))!;
        Assert.True(await _store.AddTenantMemberAsync(first.Id, member.Id, Now.AddMinutes(3)));

        EffectiveAuthorizationContext firstContext = await _store.GetEffectiveAuthorizationContextAsync(member.Id, first.Id);
        EffectiveAuthorizationContext secondContext = await _store.GetEffectiveAuthorizationContextAsync(member.Id, second.Id);

        Assert.Contains(TenantAuthRoles.Member, firstContext.Tenant!.Roles);
        Assert.DoesNotContain(TenantAuthRoles.Member, secondContext.Tenant!.Roles);
        Assert.DoesNotContain(TenantAuthPermissions.TenantRead, secondContext.Tenant.Permissions);
        Assert.False(await _store.IsTenantMemberAsync(member.Id, second.Id));
    }

    // Verifies that ownership transfer is owner-authorized, member-bound, atomic, role-aware, and version-invalidating.
    [Fact]
    public async Task TenantOwnershipTransferRequiresOwnerAndExistingMember()
    {
        AuthUser owner = await CreateUserAsync(emailVerified: true);
        AuthUser member = await CreateUserAsync(emailVerified: true);
        AuthUser outsider = await CreateUserAsync(emailVerified: true);
        TenantRecord tenant = (await _store.CreateTenantAsync(
            "Ownership Tenant",
            $"ownership-{Guid.NewGuid():N}",
            owner.Id,
            Now.AddMinutes(1)))!;

        TenantOwnershipTransferResult outsiderTransfer = await _store.TransferTenantOwnershipAsync(
            tenant.Id,
            owner.Id,
            outsider.Id,
            Now.AddMinutes(2));
        TenantOwnershipTransferResult wrongActorTransfer = await _store.TransferTenantOwnershipAsync(
            tenant.Id,
            member.Id,
            owner.Id,
            Now.AddMinutes(3));
        Assert.True(await _store.AddTenantMemberAsync(tenant.Id, member.Id, Now.AddMinutes(4)));
        AuthUser ownerBefore = (await _store.FindUserByIdAsync(owner.Id))!;
        AuthUser memberBefore = (await _store.FindUserByIdAsync(member.Id))!;

        TenantOwnershipTransferResult transferred = await _store.TransferTenantOwnershipAsync(
            tenant.Id,
            owner.Id,
            member.Id,
            Now.AddMinutes(5));
        TenantOwnerRecord? persistedOwner = await _store.GetTenantOwnerAsync(tenant.Id);
        IReadOnlyList<TenantMemberRecord> members = (await _store.ListTenantMembersAsync(tenant.Id, new AuthPageQuery(200, null))).Items;
        EffectiveAuthorizationContext previousOwnerContext = await _store.GetEffectiveAuthorizationContextAsync(owner.Id, tenant.Id);
        EffectiveAuthorizationContext newOwnerContext = await _store.GetEffectiveAuthorizationContextAsync(member.Id, tenant.Id);
        AuthUser ownerAfter = (await _store.FindUserByIdAsync(owner.Id))!;
        AuthUser memberAfter = (await _store.FindUserByIdAsync(member.Id))!;

        Assert.Equal(TenantOwnershipTransferStatus.NewOwnerNotMember, outsiderTransfer.Status);
        Assert.Equal(TenantOwnershipTransferStatus.CurrentOwnerMismatch, wrongActorTransfer.Status);
        Assert.Equal(TenantOwnershipTransferStatus.Success, transferred.Status);
        Assert.Equal(member.Id, persistedOwner?.UserId);
        Assert.Contains(members, item =>
            item.UserId == owner.Id
            && !item.IsOwner
            && !item.Roles.Contains(TenantAuthRoles.Owner));
        Assert.Contains(members, item =>
            item.UserId == member.Id
            && item.IsOwner
            && item.Roles.Contains(TenantAuthRoles.Owner));
        Assert.False(previousOwnerContext.Tenant!.IsOwner);
        Assert.DoesNotContain(TenantAuthRoles.Owner, previousOwnerContext.Tenant.Roles);
        Assert.True(newOwnerContext.Tenant!.IsOwner);
        Assert.Contains(TenantAuthRoles.Owner, newOwnerContext.Tenant.Roles);
        Assert.True(ownerAfter.SecurityVersion > ownerBefore.SecurityVersion);
        Assert.True(memberAfter.SecurityVersion > memberBefore.SecurityVersion);
    }

    // Verifies that security audit records are persisted in reverse chronological order.
    [Fact]
    public async Task AuditRecordsPersistInReverseChronologicalOrder()
    {
        await _store.InitializeAsync();
        AuditRecord older = new(
            Guid.NewGuid(),
            Now.AddMinutes(1),
            "contract.older",
            UserId: null,
            TenantId: null,
            IpAddress: "127.0.0.1",
            UserAgent: "provider-contract-test",
            Detail: "older");
        AuditRecord newer = older with
        {
            Id = Guid.NewGuid(),
            CreatedUtc = Now.AddMinutes(2),
            EventType = "contract.newer",
            Detail = "newer"
        };

        await _store.WriteAuditAsync(older);
        await _store.WriteAuditAsync(newer);

        IReadOnlyList<AuditRecord> records = (await _store.ListAuditAsync(new AuthPageQuery(10, null))).Items;

        Assert.True(records.Count >= 2);
        Assert.Equal(newer.Id, records[0].Id);
        Assert.Equal(older.Id, records[1].Id);
        Assert.Equal("newer", records[0].Detail);
        Assert.Equal("older", records[1].Detail);
    }

    // Verifies that revoking all user refresh tokens affects only the selected user.
    [Fact]
    public async Task RevokeAllUserRefreshTokensRevokesOnlySelectedUserTokens()
    {
        AuthUser selectedUser = await CreateUserAsync(emailVerified: true);
        AuthUser otherUser = await CreateUserAsync(emailVerified: true);
        RefreshTokenRecord first = CreateRefreshToken(selectedUser, tokenHashPrefix: "selected-one");
        RefreshTokenRecord second = CreateRefreshToken(selectedUser, tokenHashPrefix: "selected-two");
        RefreshTokenRecord untouched = CreateRefreshToken(otherUser, tokenHashPrefix: "other");
        DateTimeOffset revokedUtc = Now.AddMinutes(5);
        await _store.CreateRefreshTokenAsync(first);
        await _store.CreateRefreshTokenAsync(second);
        await _store.CreateRefreshTokenAsync(untouched);

        int revoked = await _store.RevokeAllUserRefreshTokensAsync(selectedUser.Id, revokedUtc);
        int revokedAgain = await _store.RevokeAllUserRefreshTokensAsync(selectedUser.Id, revokedUtc.AddMinutes(1));

        Assert.Equal(2, revoked);
        Assert.Equal(0, revokedAgain);
        Assert.Equal(revokedUtc, (await _store.FindRefreshTokenByHashAsync(first.TokenHash))?.RevokedUtc);
        Assert.Equal(revokedUtc, (await _store.FindRefreshTokenByHashAsync(second.TokenHash))?.RevokedUtc);
        Assert.Null((await _store.FindRefreshTokenByHashAsync(untouched.TokenHash))?.RevokedUtc);
    }

    protected abstract object CreateProviderStore();

    protected virtual Task DisposeProviderResourcesAsync() => Task.CompletedTask;

    private async Task<AuthUser> CreateUserAsync(bool emailVerified = false)
    {
        await _store.InitializeAsync();

        Guid id = Guid.NewGuid();
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
            CreatedUtc: Now,
            UpdatedUtc: Now);

        bool created = await _store.CreateUserWithVerificationTokenAsync(
            user,
            $"verification-{Guid.NewGuid():N}",
            Now.AddHours(1));
        Assert.True(created);
        return user;
    }

    private static RefreshTokenRecord CreateRefreshToken(AuthUser user, string tokenHashPrefix) =>
        new(
            Guid.NewGuid(),
            user.Id,
            $"{tokenHashPrefix}-{Guid.NewGuid():N}",
            Guid.NewGuid(),
            user.SecurityVersion,
            "127.0.0.1",
            "provider-contract-test",
            Now,
            Now,
            Now.AddDays(30),
            RevokedUtc: null,
            ReplacedByTokenId: null);

    private static IAuthStore RequireAuthStore(object store) =>
        store as IAuthStore ?? throw new InvalidOperationException("The provider test fixture did not create an IAuthStore instance.");
}
