using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.Persistence;
using SharpAccess.Services;

namespace SharpAccess.UnitTests.Services;

public sealed class PaginationServiceTests
{
    // Verifies Core rejects invalid public pagination input before any persistence capability is called.
    [Theory]
    [InlineData(null, 0)]
    [InlineData(null, 201)]
    [InlineData(" ", 10)]
    [InlineData("invalid", 10)]
    public async Task InvalidAdminPagesDoNotCallStore(string? cursor, int limit)
    {
        CapturingAdministrationStore store = new();
        AdministrationService service = CreateService(store, out _);

        ServiceResult<SharpAccessPage<AuthUser>> result = await service.ListUsersAsync(
            new SharpAccessPageRequest(cursor, limit));

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidInput, result.Error);
        Assert.Equal("invalid_page", result.Code);
        Assert.Equal(0, store.PageCalls);
    }

    // Verifies a valid cursor cannot cross from the user collection into the role collection.
    [Fact]
    public async Task CrossCollectionCursorDoesNotCallStore()
    {
        CapturingAdministrationStore store = new();
        AdministrationService service = CreateService(store, out AuthPageCursorCodec codec);
        SharpAccessPage<string> usersPage = codec.CreatePage(
            new AuthPageSlice<string>(["user"], new AuthPageBoundary(DateTimeOffset.UtcNow, Guid.NewGuid())),
            AuthPageCursorCodec.UsersScope,
            null);

        ServiceResult<SharpAccessPage<RoleRecord>> result = await service.ListRolesAsync(
            new SharpAccessPageRequest(usersPage.NextCursor, 10));

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_page", result.Code);
        Assert.Equal(0, store.PageCalls);
    }

    // Verifies tenant cursors are checked for route isolation before even the membership authorization read.
    [Fact]
    public async Task CrossTenantMemberCursorDoesNotCallStore()
    {
        Guid firstTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid secondTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Guid actor = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        AuthPageCursorCodec codec = new(new EphemeralDataProtectionProvider());
        string cursor = codec.CreatePage(
            new AuthPageSlice<string>(["member"], new AuthPageBoundary(DateTimeOffset.UtcNow, Guid.NewGuid())),
            AuthPageCursorCodec.TenantMembersScope,
            firstTenant).NextCursor!;
        CapturingTenantStore store = new();
        TenantService service = new(store, null!, null!, codec);

        ServiceResult<SharpAccessPage<TenantMemberRecord>> result = await service.ListMembersAsync(
            secondTenant,
            actor,
            new SharpAccessPageRequest(cursor, 10));

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_page", result.Code);
        Assert.Equal(0, store.Calls);
    }

    // Verifies invalid owner identifiers are rejected before persistence access.
    [Fact]
    public async Task InvalidTenantOwnerRequestDoesNotCallStore()
    {
        CapturingTenantStore store = new();
        TenantService service = CreateTenantService(store);

        ServiceResult<TenantOwnerRecord> result = await service.GetOwnerAsync(Guid.Empty, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidInput, result.Error);
        Assert.Equal("invalid_tenant_owner", result.Code);
        Assert.Equal(0, store.Calls);
    }

    // Verifies a non-member cannot discover the tenant owner.
    [Fact]
    public async Task NonMemberCannotReadTenantOwner()
    {
        CapturingTenantStore store = new() { IsMember = false };
        TenantService service = CreateTenantService(store);

        ServiceResult<TenantOwnerRecord> result = await service.GetOwnerAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.Forbidden, result.Error);
        Assert.Equal("tenant_access_denied", result.Code);
        Assert.Equal(1, store.Calls);
        Assert.Equal(0, store.OwnerCalls);
    }

    // Verifies an accessible tenant with missing owner state fails explicitly.
    [Fact]
    public async Task MissingTenantOwnerReturnsNotFound()
    {
        CapturingTenantStore store = new();
        TenantService service = CreateTenantService(store);

        ServiceResult<TenantOwnerRecord> result = await service.GetOwnerAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.NotFound, result.Error);
        Assert.Equal("tenant_owner_not_found", result.Code);
        Assert.Equal(1, store.OwnerCalls);
    }

    // Verifies an active member receives the immutable owner record.
    [Fact]
    public async Task MemberCanReadTenantOwner()
    {
        Guid tenantId = Guid.NewGuid();
        TenantOwnerRecord owner = new(tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        CapturingTenantStore store = new() { Owner = owner };
        TenantService service = CreateTenantService(store);

        ServiceResult<TenantOwnerRecord> result = await service.GetOwnerAsync(tenantId, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(owner, result.Value);
        Assert.Equal(1, store.OwnerCalls);
    }

    // Verifies an invalid list query is surfaced by the endpoint as a sanitized bad request.
    [Fact]
    public async Task InvalidAdminPageEndpointReturnsBadRequestWithoutCallingStore()
    {
        CapturingAdministrationStore store = new();
        AdministrationService service = CreateService(store, out _);
        IResult result = await AdminEndpointHandlers.ListUsersAsync(
            "not-a-cursor",
            10,
            service,
            CancellationToken.None);
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        DefaultHttpContext context = new() { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, store.PageCalls);
    }

    // Creates a real protected-cursor service around a persistence-call probe.
    private static AdministrationService CreateService(
        CapturingAdministrationStore store,
        out AuthPageCursorCodec codec)
    {
        codec = new AuthPageCursorCodec(new EphemeralDataProtectionProvider());
        return new AdministrationService(store, null!, null!, codec);
    }

    private static TenantService CreateTenantService(CapturingTenantStore store) =>
        new(store, null!, null!, new AuthPageCursorCodec(new EphemeralDataProtectionProvider()));

    private sealed class CapturingAdministrationStore : IAuthAdministrationStore
    {
        public int PageCalls { get; private set; }

        // Records a user page call.
        public Task<AuthPageSlice<AuthUser>> ListUsersAsync(AuthPageQuery page, CancellationToken cancellationToken = default)
        {
            PageCalls++;
            return Task.FromResult(new AuthPageSlice<AuthUser>([], null));
        }

        // Records a role page call.
        public Task<AuthPageSlice<RoleRecord>> ListRolesAsync(AuthPageQuery page, CancellationToken cancellationToken = default)
        {
            PageCalls++;
            return Task.FromResult(new AuthPageSlice<RoleRecord>([], null));
        }

        // Records a permission page call.
        public Task<AuthPageSlice<PermissionRecord>> ListPermissionsAsync(AuthPageQuery page, CancellationToken cancellationToken = default)
        {
            PageCalls++;
            return Task.FromResult(new AuthPageSlice<PermissionRecord>([], null));
        }

        // Records an audit page call.
        public Task<AuthPageSlice<AuditRecord>> ListAuditAsync(AuthPageQuery page, CancellationToken cancellationToken = default)
        {
            PageCalls++;
            return Task.FromResult(new AuthPageSlice<AuditRecord>([], null));
        }

        // Rejects unused account creation in this page-only test double.
        public Task<bool> CreateUserWithVerificationTokenAsync(AuthUser user, string verificationTokenHash, DateTimeOffset verificationExpiresUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused email lookup in this page-only test double.
        public Task<AuthUser?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused identifier lookup in this page-only test double.
        public Task<AuthUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused lockout mutation in this page-only test double.
        public Task RecordLoginFailureAsync(Guid userId, int failureThreshold, DateTimeOffset lockoutEndUtc, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused lockout reset in this page-only test double.
        public Task ResetLoginFailuresAsync(Guid userId, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused password rehash in this page-only test double.
        public Task<bool> UpdatePasswordHashAsync(Guid userId, string expectedPasswordHash, int expectedSecurityVersion, string passwordHash, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused password change in this page-only test double.
        public Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused user status mutation in this page-only test double.
        public Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused role creation in this page-only test double.
        public Task<RoleRecord?> CreateRoleAsync(string name, string normalizedName, string description, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused role update in this page-only test double.
        public Task<bool> UpdateRoleAsync(Guid roleId, string name, string normalizedName, string description, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused role-permission assignment in this page-only test double.
        public Task<bool> AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused role-permission removal in this page-only test double.
        public Task<bool> RemovePermissionFromRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused user-role assignment in this page-only test double.
        public Task<bool> AssignGlobalRoleToUserAsync(Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused user-role removal in this page-only test double.
        public Task<bool> RemoveGlobalRoleFromUserAsync(Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused audit writes in this page-only test double.
        public Task WriteAuditAsync(AuditRecord audit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Delegates evidence-bearing password changes for this page-only test double.
        public Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => ChangePasswordAsync(userId, passwordHash, now, cancellationToken);
        // Delegates evidence-bearing user status changes for this page-only test double.
        public Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => SetUserActiveAsync(userId, isActive, now, cancellationToken);
        // Delegates evidence-bearing role creation for this page-only test double.
        public Task<RoleRecord?> CreateRoleAsync(string name, string normalizedName, string description, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => CreateRoleAsync(name, normalizedName, description, now, cancellationToken);
        // Delegates evidence-bearing role updates for this page-only test double.
        public Task<bool> UpdateRoleAsync(Guid roleId, string name, string normalizedName, string description, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => UpdateRoleAsync(roleId, name, normalizedName, description, now, cancellationToken);
        // Delegates evidence-bearing permission assignments for this page-only test double.
        public Task<bool> AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => AssignPermissionToRoleAsync(roleId, permissionId, now, cancellationToken);
        // Delegates evidence-bearing permission removals for this page-only test double.
        public Task<bool> RemovePermissionFromRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RemovePermissionFromRoleAsync(roleId, permissionId, now, cancellationToken);
        // Delegates evidence-bearing global role assignments for this page-only test double.
        public Task<bool> AssignGlobalRoleToUserAsync(Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => AssignGlobalRoleToUserAsync(userId, roleId, now, cancellationToken);
        // Delegates evidence-bearing global role removals for this page-only test double.
        public Task<bool> RemoveGlobalRoleFromUserAsync(Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RemoveGlobalRoleFromUserAsync(userId, roleId, now, cancellationToken);
    }

    private sealed class CapturingTenantStore : IAuthTenantManagementStore
    {
        public int Calls { get; private set; }
        public int OwnerCalls { get; private set; }
        public bool IsMember { get; init; } = true;
        public TenantOwnerRecord? Owner { get; init; }

        // Records membership authorization reads.
        public Task<bool> IsTenantMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(IsMember);
        }

        // Records tenant list reads.
        public Task<AuthPageSlice<TenantRecord>> ListTenantsForUserAsync(Guid userId, AuthPageQuery page, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AuthPageSlice<TenantRecord>([], null));
        }

        // Records member list reads.
        public Task<AuthPageSlice<TenantMemberRecord>> ListTenantMembersAsync(Guid tenantId, AuthPageQuery page, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AuthPageSlice<TenantMemberRecord>([], null));
        }

        // Rejects unused tenant creation in this page-only test double.
        public Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused tenant lookup in this page-only test double.
        public Task<TenantRecord?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Records owner lookup without requiring provider infrastructure.
        public Task<TenantOwnerRecord?> GetTenantOwnerAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            OwnerCalls++;
            return Task.FromResult(Owner);
        }
        // Rejects unused ownership transfer in this page-only test double.
        public Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused membership creation in this page-only test double.
        public Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused tenant-role assignment in this page-only test double.
        public Task<bool> AssignTenantRoleToUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Rejects unused tenant-role removal in this page-only test double.
        public Task<bool> RemoveTenantRoleFromUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        // Delegates evidence-bearing tenant creation for this page-only test double.
        public Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => CreateTenantAsync(name, slug, ownerUserId, now, cancellationToken);
        // Delegates evidence-bearing ownership transfers for this page-only test double.
        public Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => TransferTenantOwnershipAsync(tenantId, currentOwnerUserId, newOwnerUserId, now, cancellationToken);
        // Delegates evidence-bearing membership creation for this page-only test double.
        public Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => AddTenantMemberAsync(tenantId, userId, now, cancellationToken);
        // Delegates evidence-bearing tenant role assignments for this page-only test double.
        public Task<bool> AssignTenantRoleToUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => AssignTenantRoleToUserAsync(tenantId, userId, roleId, now, cancellationToken);
        // Delegates evidence-bearing tenant role removals for this page-only test double.
        public Task<bool> RemoveTenantRoleFromUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) => RemoveTenantRoleFromUserAsync(tenantId, userId, roleId, now, cancellationToken);
    }
}
