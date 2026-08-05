using System.Security.Claims;
using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SharpAccess.UnitTests;

public sealed class AdminAndTenantEndpointHandlerTests
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails()
        .BuildServiceProvider();

    private static readonly Guid ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RoleId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PermissionId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TenantId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TenantCreateListGetAndOwnerHandlersMapSuccessfulResults()
    {
        FakeTenantService service = new();
        DefaultHttpContext context = CreateContext(ActorId, tenantId: TenantId, readAllTenants: true);

        IResult created = await TenantEndpointHandlers.CreateAsync(
            new CreateTenantRequest("Tenant", "tenant"),
            context,
            service,
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status201Created, await ExecuteAndGetStatusAsync(created));
        Assert.Equal(ActorId, service.LastOwnerUserId);

        IResult listed = await TenantEndpointHandlers.ListAsync(null, null, context, service, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(listed));

        IResult found = await TenantEndpointHandlers.GetAsync(TenantId, context, service, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(found));
        Assert.True(service.LastCanManageAll);

        IResult owner = await TenantEndpointHandlers.GetOwnerAsync(
            TenantId,
            context,
            service,
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(owner));

        IResult transfer = await TenantEndpointHandlers.TransferOwnershipAsync(
            TenantId,
            new TransferTenantOwnershipRequest(UserId),
            context,
            service,
            CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(transfer));
        Assert.Equal(UserId, service.LastTransferredOwnerUserId);
    }

    [Fact]
    public async Task TenantHandlersReturnProblemsForMissingOrMismatchedClaims()
    {
        FakeTenantService service = new();
        DefaultHttpContext anonymous = CreateContext();

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await TenantEndpointHandlers.CreateAsync(
                new CreateTenantRequest("Tenant", "tenant"),
                anonymous,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await TenantEndpointHandlers.ListAsync(null, null, anonymous, service, CancellationToken.None)));

        DefaultHttpContext wrongTenant = CreateContext(
            ActorId,
            tenantId: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Assert.Equal(StatusCodes.Status403Forbidden, await ExecuteAndGetStatusAsync(
            await TenantEndpointHandlers.AddMemberAsync(
                TenantId,
                new AddTenantMemberRequest(UserId),
                wrongTenant,
                service,
                CancellationToken.None)));
    }

    [Fact]
    public async Task TenantMemberHandlersMapSuccessAndServiceFailures()
    {
        FakeTenantService service = new();
        DefaultHttpContext context = CreateContext(ActorId, tenantId: TenantId);

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await TenantEndpointHandlers.AddMemberAsync(
                TenantId,
                new AddTenantMemberRequest(UserId),
                context,
                service,
                CancellationToken.None)));
        Assert.Equal(UserId, service.LastMemberUserId);

        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(
            await TenantEndpointHandlers.ListMembersAsync(TenantId, null, null, context, service, CancellationToken.None)));

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await TenantEndpointHandlers.AssignMemberRoleAsync(
                TenantId,
                UserId,
                new AssignTenantRoleRequest(RoleId),
                context,
                service,
                CancellationToken.None)));
        Assert.Equal(RoleId, service.LastRoleId);

        service.NextBoolFailure = ServiceResult<bool>.Failure(AuthError.NotFound, "tenant_role_not_assigned");
        Assert.Equal(StatusCodes.Status404NotFound, await ExecuteAndGetStatusAsync(
            await TenantEndpointHandlers.AssignMemberRoleAsync(
                TenantId,
                UserId,
                new AssignTenantRoleRequest(RoleId),
                context,
                service,
                CancellationToken.None)));
    }

    [Fact]
    public async Task AdminListHandlersMapCollectionsAndDefaultPagination()
    {
        FakeAdministrationService service = new();

        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.ListUsersAsync(null, null, service, CancellationToken.None)));
        Assert.Equal((null, 100), service.LastUserPage);

        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.ListRolesAsync(null, null, service, CancellationToken.None)));

        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.ListPermissionsAsync(null, null, service, CancellationToken.None)));

        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.ListAuditAsync("cursor", 10, service, CancellationToken.None)));
        Assert.Equal(("cursor", 10), service.LastAuditPage);
    }

    [Fact]
    public async Task AdminWriteHandlersRequireAuthenticatedActors()
    {
        FakeAdministrationService service = new();
        DefaultHttpContext anonymous = CreateContext();

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.SetUserStatusAsync(
                UserId,
                new SetUserStatusRequest(true),
                anonymous,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.CreateRoleAsync(
                new CreateRoleRequest("role", "desc"),
                anonymous,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.AssignPermissionAsync(
                RoleId,
                new AssignPermissionRequest(PermissionId),
                anonymous,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.RemoveUserRoleAsync(
                UserId,
                RoleId,
                anonymous,
                service,
                CancellationToken.None)));
    }

    [Fact]
    public async Task AdminWriteHandlersMapGlobalOnlySuccessAndFailureResults()
    {
        FakeAdministrationService service = new();
        DefaultHttpContext context = CreateContext(ActorId);

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.SetUserStatusAsync(
                UserId,
                new SetUserStatusRequest(false),
                context,
                service,
                CancellationToken.None)));
        Assert.Equal(ActorId, service.LastActorUserId);

        Assert.Equal(StatusCodes.Status201Created, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.CreateRoleAsync(
                new CreateRoleRequest("role", "desc"),
                context,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.UpdateRoleAsync(
                RoleId,
                new UpdateRoleRequest("role", "desc"),
                context,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.AssignPermissionAsync(
                RoleId,
                new AssignPermissionRequest(PermissionId),
                context,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.RemovePermissionAsync(
                RoleId,
                PermissionId,
                context,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.AssignUserRoleAsync(
                UserId,
                new AssignRoleRequest(RoleId),
                context,
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.RemoveUserRoleAsync(
                UserId,
                RoleId,
                context,
                service,
                CancellationToken.None)));

        service.NextRoleFailure = ServiceResult<RoleRecord>.Failure(AuthError.Conflict, "role_exists");
        Assert.Equal(StatusCodes.Status409Conflict, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.CreateRoleAsync(
                new CreateRoleRequest("role", "desc"),
                context,
                service,
                CancellationToken.None)));

        service.NextBoolFailure = ServiceResult<bool>.Failure(AuthError.NotFound, "role_not_found");
        Assert.Equal(StatusCodes.Status404NotFound, await ExecuteAndGetStatusAsync(
            await AdminEndpointHandlers.UpdateRoleAsync(
                RoleId,
                new UpdateRoleRequest("role", "desc"),
                context,
                service,
                CancellationToken.None)));
    }

    private static DefaultHttpContext CreateContext(
        Guid? userId = null,
        Guid? tenantId = null,
        bool readAllTenants = false)
    {
        DefaultHttpContext context = new()
        {
            RequestServices = Services
        };
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        context.Request.Headers.UserAgent = "unit-test";

        List<Claim> claims = [];
        if (userId.HasValue)
        {
            claims.Add(new Claim("sub", userId.Value.ToString("D")));
        }

        if (tenantId.HasValue)
        {
            claims.Add(new Claim(AuthConstants.TenantClaim, tenantId.Value.ToString("D")));
        }

        if (readAllTenants)
        {
            claims.Add(new Claim(
                AuthConstants.GlobalPermissionClaim,
                AuthPermissions.TenantsRead));
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "unit"));
        return context;
    }

    private static async Task<int> ExecuteAndGetStatusAsync(IResult result)
    {
        DefaultHttpContext context = CreateContext();
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    private sealed class FakeTenantService : ITenantService
    {
        public Guid LastOwnerUserId { get; private set; }
        public Guid LastTransferredOwnerUserId { get; private set; }
        public Guid LastMemberUserId { get; private set; }
        public Guid LastRoleId { get; private set; }
        public bool LastCanManageAll { get; private set; }
        public ServiceResult<bool>? NextBoolFailure { get; set; }

        public Task<ServiceResult<TenantRecord>> CreateAsync(
            string? name,
            string? slug,
            Guid ownerUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastOwnerUserId = ownerUserId;
            return Task.FromResult(ServiceResult<TenantRecord>.Success(
                new TenantRecord(TenantId, name ?? "Tenant", slug ?? "tenant", Now)));
        }

        public Task<ServiceResult<SharpAccessPage<TenantRecord>>> ListAsync(
            Guid userId,
            SharpAccessPageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<SharpAccessPage<TenantRecord>>.Success(new SharpAccessPage<TenantRecord>([
                new TenantRecord(TenantId, "Tenant", "tenant", Now)
            ], null)));

        public Task<ServiceResult<TenantRecord>> GetAsync(
            Guid tenantId,
            Guid requestingUserId,
            bool canManageAll,
            CancellationToken cancellationToken = default)
        {
            LastCanManageAll = canManageAll;
            return Task.FromResult(ServiceResult<TenantRecord>.Success(
                new TenantRecord(tenantId, "Tenant", "tenant", Now)));
        }

        public Task<ServiceResult<TenantOwnerRecord>> GetOwnerAsync(
            Guid tenantId,
            Guid actorUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<TenantOwnerRecord>.Success(
                new TenantOwnerRecord(tenantId, ActorId, Now)));

        public Task<ServiceResult<TenantOwnerRecord>> TransferOwnershipAsync(
            Guid tenantId,
            Guid newOwnerUserId,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastTransferredOwnerUserId = newOwnerUserId;
            return Task.FromResult(ServiceResult<TenantOwnerRecord>.Success(
                new TenantOwnerRecord(tenantId, newOwnerUserId, Now)));
        }

        public Task<ServiceResult<bool>> AddMemberAsync(
            Guid tenantId,
            Guid userId,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastMemberUserId = userId;
            return Task.FromResult(ConsumeBoolResult());
        }

        public Task<ServiceResult<SharpAccessPage<TenantMemberRecord>>> ListMembersAsync(
            Guid tenantId,
            Guid actorUserId,
            SharpAccessPageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<SharpAccessPage<TenantMemberRecord>>.Success(new SharpAccessPage<TenantMemberRecord>([
                new TenantMemberRecord(UserId, "user@test.local", false, [TenantAuthRoles.Member])
            ], null)));

        public Task<ServiceResult<bool>> AssignRoleAsync(
            Guid tenantId,
            Guid userId,
            Guid roleId,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastRoleId = roleId;
            return Task.FromResult(ConsumeBoolResult());
        }

        private ServiceResult<bool> ConsumeBoolResult()
        {
            ServiceResult<bool>? failure = NextBoolFailure;
            NextBoolFailure = null;
            return failure ?? ServiceResult<bool>.Success(true);
        }
    }

    private sealed class FakeAdministrationService : IAdministrationService
    {
        public (string? Cursor, int Limit) LastUserPage { get; private set; }
        public (string? Cursor, int Limit) LastAuditPage { get; private set; }
        public Guid LastActorUserId { get; private set; }
        public ServiceResult<bool>? NextBoolFailure { get; set; }
        public ServiceResult<RoleRecord>? NextRoleFailure { get; set; }

        public Task<ServiceResult<SharpAccessPage<AuthUser>>> ListUsersAsync(
            SharpAccessPageRequest request,
            CancellationToken cancellationToken = default)
        {
            LastUserPage = (request.Cursor, request.Limit);
            return Task.FromResult(ServiceResult<SharpAccessPage<AuthUser>>.Success(new SharpAccessPage<AuthUser>([
                new AuthUser(UserId, "user@test.local", "USER@TEST.LOCAL", "hash", Now, true, 1, Now.AddMinutes(5), 2, Now, Now)
            ], null)));
        }

        public Task<ServiceResult<SharpAccessPage<RoleRecord>>> ListRolesAsync(SharpAccessPageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<SharpAccessPage<RoleRecord>>.Success(new SharpAccessPage<RoleRecord>([
                new RoleRecord(RoleId, "admin", "Admin", true)
            ], null)));

        public Task<ServiceResult<RoleRecord>> CreateRoleAsync(
            string? name,
            string? description,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastActorUserId = actorUserId;
            ServiceResult<RoleRecord>? failure = NextRoleFailure;
            NextRoleFailure = null;
            return Task.FromResult(failure ?? ServiceResult<RoleRecord>.Success(
                new RoleRecord(RoleId, name ?? "role", description ?? "desc", false)));
        }

        public Task<ServiceResult<bool>> UpdateRoleAsync(
            Guid roleId,
            string? name,
            string? description,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(ConsumeBoolResult());
        }

        public Task<ServiceResult<SharpAccessPage<PermissionRecord>>> ListPermissionsAsync(SharpAccessPageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<SharpAccessPage<PermissionRecord>>.Success(new SharpAccessPage<PermissionRecord>([
                new PermissionRecord(PermissionId, AuthPermissions.UsersRead, "Read users")
            ], null)));

        public Task<ServiceResult<bool>> AssignPermissionAsync(
            Guid roleId,
            Guid permissionId,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(ConsumeBoolResult());
        }

        public Task<ServiceResult<bool>> RemovePermissionAsync(
            Guid roleId,
            Guid permissionId,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(ConsumeBoolResult());
        }

        public Task<ServiceResult<bool>> AssignRoleAsync(
            Guid userId,
            Guid roleId,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(ConsumeBoolResult());
        }

        public Task<ServiceResult<bool>> RemoveRoleAsync(
            Guid userId,
            Guid roleId,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(ConsumeBoolResult());
        }

        public Task<ServiceResult<bool>> SetUserActiveAsync(
            Guid userId,
            bool isActive,
            Guid actorUserId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(ConsumeBoolResult());
        }

        public Task<ServiceResult<SharpAccessPage<AuditRecord>>> ListAuditAsync(
            SharpAccessPageRequest request,
            CancellationToken cancellationToken = default)
        {
            LastAuditPage = (request.Cursor, request.Limit);
            return Task.FromResult(ServiceResult<SharpAccessPage<AuditRecord>>.Success(new SharpAccessPage<AuditRecord>([
                new AuditRecord(Guid.NewGuid(), Now, "event", UserId, TenantId, "127.0.0.1", "unit-test", "detail")
            ], null)));
        }

        private ServiceResult<bool> ConsumeBoolResult()
        {
            ServiceResult<bool>? failure = NextBoolFailure;
            NextBoolFailure = null;
            return failure ?? ServiceResult<bool>.Success(true);
        }
    }
}
