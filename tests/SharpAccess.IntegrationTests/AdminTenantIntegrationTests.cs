using System.Collections.Concurrent;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SharpAccess.IntegrationTests;

public sealed class AdminTenantIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TenantManagerRoleId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private CapturingEmailSender _emails = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"sharpaccess-admin-{Guid.NewGuid():N}.db");
        _emails = new CapturingEmailSender();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IEmailSender>(_emails);
        services.AddSharpAccess(options => Configure(options));
        services.AddSqliteAccess(options => options.ConnectionString = $"Data Source={_databasePath};Pooling=False");
        _provider = services.BuildServiceProvider(validateScopes: true);
        await _provider.InitializeSharpAccessAsync();
    }

    public Task DisposeAsync()
    {
        _provider.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AdministrationAndTenantFlowsPersistScopedAuthorizationAndAuditOwnership()
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IAuthService auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        IAdministrationService administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
        ITenantService tenants = scope.ServiceProvider.GetRequiredService<ITenantService>();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        RequestMetadata metadata = new("127.0.0.1", "integration-admin-test");

        AuthUser owner = await CreateVerifiedUserAsync(auth, store, "owner@example.com", metadata);
        AuthUser member = await CreateVerifiedUserAsync(auth, store, "member@example.com", metadata);

        ServiceResult<SharpAccessPage<RoleRecord>> globalRolePage = await administration.ListRolesAsync(new SharpAccessPageRequest());
        ServiceResult<SharpAccessPage<PermissionRecord>> permissionPage = await administration.ListPermissionsAsync(new SharpAccessPageRequest());
        IReadOnlyList<RoleRecord> globalRoles = globalRolePage.Value!.Items;
        IReadOnlyList<PermissionRecord> permissions = permissionPage.Value!.Items;
        RoleRecord globalManager = Assert.Single(globalRoles, static role => role.Name == "Manager");
        PermissionRecord auditRead = Assert.Single(permissions, static permission => permission.Name == AuthPermissions.AuditRead);

        ServiceResult<RoleRecord> createdRole = await administration.CreateRoleAsync(
            "Auditor",
            "Reads audit records.",
            owner.Id,
            metadata);
        Assert.True(createdRole.Succeeded);
        Assert.NotNull(createdRole.Value);

        Assert.True((await administration.UpdateRoleAsync(
            createdRole.Value!.Id,
            "Audit Reader",
            "Reads security audit records.",
            owner.Id,
            metadata)).Succeeded);
        Assert.True((await administration.AssignPermissionAsync(createdRole.Value.Id, auditRead.Id, owner.Id, metadata)).Succeeded);
        Assert.True((await administration.RemovePermissionAsync(createdRole.Value.Id, auditRead.Id, owner.Id, metadata)).Succeeded);
        Assert.True((await administration.AssignRoleAsync(member.Id, createdRole.Value.Id, owner.Id, metadata)).Succeeded);
        Assert.True((await administration.RemoveRoleAsync(member.Id, createdRole.Value.Id, owner.Id, metadata)).Succeeded);
        Assert.True((await administration.SetUserActiveAsync(member.Id, false, owner.Id, metadata)).Succeeded);
        Assert.True((await administration.SetUserActiveAsync(member.Id, true, owner.Id, metadata)).Succeeded);
        ServiceResult<SharpAccessPage<AuthUser>> userPage = await administration.ListUsersAsync(new SharpAccessPageRequest(Limit: 10));
        Assert.True(userPage.Value!.Items.Count >= 2);

        ServiceResult<TenantRecord> tenant = await tenants.CreateAsync("Acme Workspace", "acme-workspace", owner.Id, metadata);
        Assert.True(tenant.Succeeded);
        Assert.NotNull(tenant.Value);
        ServiceResult<SharpAccessPage<TenantRecord>> tenantPage = await tenants.ListAsync(owner.Id, new SharpAccessPageRequest());
        Assert.Single(tenantPage.Value!.Items);
        Assert.True((await tenants.GetAsync(tenant.Value!.Id, owner.Id, canManageAll: false)).Succeeded);
        Assert.False((await tenants.GetAsync(tenant.Value.Id, member.Id, canManageAll: false)).Succeeded);
        Assert.True((await tenants.AddMemberAsync(tenant.Value.Id, member.Id, owner.Id, metadata)).Succeeded);

        ServiceResult<bool> leakedGlobalRole = await tenants.AssignRoleAsync(
            tenant.Value.Id,
            member.Id,
            globalManager.Id,
            owner.Id,
            metadata);
        Assert.False(leakedGlobalRole.Succeeded);
        Assert.True((await tenants.AssignRoleAsync(
            tenant.Value.Id,
            member.Id,
            TenantManagerRoleId,
            owner.Id,
            metadata)).Succeeded);

        ServiceResult<SharpAccessPage<TenantMemberRecord>> members = await tenants.ListMembersAsync(tenant.Value.Id, owner.Id, new SharpAccessPageRequest());
        Assert.True(members.Succeeded);
        Assert.Equal(2, members.Value!.Items.Count);
        Assert.Contains(members.Value.Items, item =>
            item.UserId == owner.Id
            && item.IsOwner
            && item.Roles.Contains(TenantAuthRoles.Owner, StringComparer.Ordinal));
        Assert.Contains(members.Value.Items, item =>
            item.UserId == member.Id
            && item.Roles.Contains(TenantAuthRoles.Manager, StringComparer.Ordinal));

        ServiceResult<TenantOwnerRecord> transferred = await tenants.TransferOwnershipAsync(
            tenant.Value.Id,
            member.Id,
            owner.Id,
            metadata);
        Assert.True(transferred.Succeeded);
        Assert.Equal(member.Id, transferred.Value?.UserId);

        EffectiveAuthorizationContext previousOwner = await store.GetEffectiveAuthorizationContextAsync(owner.Id, tenant.Value.Id);
        EffectiveAuthorizationContext newOwner = await store.GetEffectiveAuthorizationContextAsync(member.Id, tenant.Value.Id);
        Assert.False(previousOwner.Tenant!.IsOwner);
        Assert.DoesNotContain(TenantAuthRoles.Owner, previousOwner.Tenant.Roles);
        Assert.True(newOwner.Tenant!.IsOwner);
        Assert.Contains(TenantAuthRoles.Owner, newOwner.Tenant.Roles);
        Assert.DoesNotContain(AuthRoles.Admin, newOwner.Tenant.Roles);

        ServiceResult<SharpAccessPage<AuditRecord>> auditPage = await administration.ListAuditAsync(new SharpAccessPageRequest());
        IReadOnlyList<AuditRecord> auditRecords = auditPage.Value!.Items;
        Assert.Contains(auditRecords, record => record.EventType == "tenant_created" && record.TenantId == tenant.Value.Id);
        Assert.Contains(auditRecords, record => record.EventType == "tenant_role_assigned" && record.TenantId == tenant.Value.Id);
        AuditRecord ownershipAudit = Assert.Single(
            auditRecords,
            record => record.EventType == "tenant_ownership_transferred" && record.TenantId == tenant.Value.Id);
        Assert.Equal(member.Id, ownershipAudit.UserId);
        Assert.DoesNotContain(auditRecords, record =>
            record.EventType is "tenant_ownership_transfer_started" or "tenant_ownership_transfer_completed");
    }

    private async Task<AuthUser> CreateVerifiedUserAsync(
        IAuthService auth,
        IAuthStore store,
        string email,
        RequestMetadata metadata)
    {
        _emails.Messages.Clear();
        Assert.True((await auth.RegisterAsync(email, "ValidPassword123", metadata)).Succeeded);
        string token = ExtractFragmentToken(_emails.Messages.Single().TextBody, "verify_token");
        Assert.True((await auth.VerifyEmailAsync(token, metadata)).Succeeded);
        IReadOnlyList<AuthUser> users = (await store.ListUsersAsync(new AuthPageQuery(50, null))).Items;
        return Assert.Single(users, user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private static void Configure(AuthOptions options)
    {
        options.BaseUri = new Uri("https://app.test");
        options.JwtIssuer = "integration-tests";
        options.JwtAudience = "integration-clients";
        options.JwtSigningKey = "INTEGRATION-JWT-SIGNING-KEY-123456789012345678";
        options.Features.PasswordAuthentication = true;
        options.Features.Registration = true;
        options.Features.PasswordReset = true;
        options.Features.RefreshTokens = true;
        options.Features.Administration = true;
        options.Features.Tenancy = true;
        options.TokenHashing.Key = "INTEGRATION-TOKEN-HASH-KEY-123456789012345678";
        options.RateLimits.PartitionKey = "INTEGRATION-RATE-LIMIT-KEY-12345678901234567890";
        options.Passwords.Iterations = 1;
        options.Passwords.MemorySizeKiB = 8_192;
        options.Passwords.DegreeOfParallelism = 1;
        options.Passwords.Peppers["v1"] = "INTEGRATION-PASSWORD-PEPPER-123456789012345";
        options.RefreshCookieSecurePolicy = CookieSecurePolicy.Always;
        options.RefreshTokenCookieName = "__Secure-sharpaccess_refresh";
        options.RequireCsrfHeaderForCookieRefreshRequests = true;
        options.Migrations.Mode = SharpAccessMigrationMode.ApplyAtStartup;
    }

    private static string ExtractFragmentToken(string text, string name)
    {
        int marker = text.IndexOf($"#{name}=", StringComparison.Ordinal);
        Assert.True(marker >= 0);
        string encoded = text[(marker + name.Length + 2)..].Trim();
        return Uri.UnescapeDataString(encoded);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        internal ConcurrentBag<AuthEmailMessage> Messages { get; } = [];

        public Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
