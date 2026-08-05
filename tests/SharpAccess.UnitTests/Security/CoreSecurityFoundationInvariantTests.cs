using SharpAccess.Abstractions;
using SharpAccess.Attributes;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Middleware;
using SharpAccess.Security;
using SharpAccess.Tokens;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;

namespace SharpAccess.UnitTests;

public sealed class CoreSecurityFoundationInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuiltInPermissionListContainsEveryPermission()
    {
        Assert.Equal(
            [
                AuthPermissions.UsersRead,
                AuthPermissions.UsersManage,
                AuthPermissions.RolesRead,
                AuthPermissions.RolesManage,
                AuthPermissions.PermissionsRead,
                AuthPermissions.PermissionsManage,
                AuthPermissions.SessionsManage,
                AuthPermissions.AuditRead,
                AuthPermissions.TenantsRead,
                AuthPermissions.TenantsManage,
                AuthPermissions.ProfileRead,
                AuthPermissions.ProfileUpdate
            ],
            AuthPermissions.All);
    }

    [Fact]
    public async Task MissingEmailSenderValidatesCancellationAndFailsClearly()
    {
        MissingEmailSender sender = new();
        AuthEmailMessage message = new("person@example.com", "Subject", "Body");

        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.SendAsync(null!));

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(message, cancelled.Token));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(message));

        Assert.Contains("no IEmailSender implementation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HmacTokenProtectorValidatesInputsAndDisposal()
    {
        HmacTokenProtector protector = new(Options.Create(TestOptions.Create()));

        Assert.Throws<ArgumentOutOfRangeException>(() => protector.Generate(31));
        Assert.Throws<ArgumentOutOfRangeException>(() => protector.Generate(129));
        Assert.Throws<ArgumentException>(() => protector.Hash(" "));
        Assert.Throws<ArgumentException>(() => protector.Hash(new string('a', 1_025)));

        protector.Dispose();
        protector.Dispose();

        Assert.Throws<ObjectDisposedException>(() => protector.Generate());
        Assert.Throws<ObjectDisposedException>(() => protector.Hash("token"));
    }

    [Fact]
    public void JwtAccessTokenServiceAcceptsBase64KeysAndRejectsNullUsers()
    {
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(new string('x', 32)));

        using JwtAccessTokenService service = new(Options.Create(options), new FixedClock());

        Assert.Throws<ArgumentNullException>(() => service.Create(null!));

        AccessTokenResult token = service.Create(new UserContext(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "person@example.com",
            true,
            [],
            [],
            null,
            1));

        Assert.False(string.IsNullOrWhiteSpace(token.Token));
    }

    // Verifies explicit global and active-tenant authorization attribute validation branches.
    [Fact]
    public void AuthorizationAttributesCoverValidAndInvalidBranches()
    {
        RequireGlobalPermissionAttribute permission = new(" users.read ");
        Assert.Equal("users.read", permission.Permission);

        RequireAllGlobalPermissionsAttribute all = new(" users.read ", "users.read", "roles.read");
        Assert.Equal(["users.read", "roles.read"], all.Permissions);

        RequireActiveTenantAttribute defaultTenant = new();
        Assert.Equal("tenantId", defaultTenant.RouteParameterName);

        RequireActiveTenantAttribute customTenant = new("workspace_id");
        Assert.Equal("workspace_id", customTenant.RouteParameterName);

        Assert.Throws<ArgumentNullException>(() => new RequireGlobalRoleAttribute((string[])null!));
        Assert.Throws<ArgumentException>(() => new RequireActiveTenantAttribute("bad-name"));
        Assert.Throws<ArgumentException>(() => new RequireActiveTenantAttribute(new string('a', 101)));
        Assert.Throws<ArgumentNullException>(() => new RequireAllGlobalPermissionsAttribute((string[])null!));
        Assert.Throws<ArgumentException>(() => new RequireAllGlobalPermissionsAttribute("users.read\u0001"));
    }

    [Fact]
    public async Task CookieRequestHeaderMiddlewareRejectsUnconfirmedCookieRefresh()
    {
        bool nextCalled = false;
        CookieRequestHeaderMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        AuthOptions options = new()
        {
            RequireCsrfHeaderForCookieRefreshRequests = true,
            RefreshTokenCookieName = AuthConstants.DefaultRefreshTokenCookieName,
            RefreshTokenCookiePath = "/auth",
            CsrfHeaderName = AuthConstants.DefaultCsrfHeaderName,
            CsrfHeaderValue = "1"
        };
        DefaultHttpContext context = Context(HttpMethods.Post, "/auth/refresh");
        context.Request.Headers.Cookie = $"{AuthConstants.DefaultRefreshTokenCookieName}=token";

        await middleware.InvokeAsync(context, Options.Create(options));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task FreshAuthenticationMiddlewareRejectsStaleAuthenticatedMutation()
    {
        bool nextCalled = false;
        FreshAuthenticationMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = Context(HttpMethods.Delete, "/tenants/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AuthConstants.AuthenticationTimeClaim, Now.AddMinutes(-11).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))
        ], "unit"));

        AuthOptions options = TestOptions.Create();
        options.FreshAuthenticationMinutes = 10;

        await middleware.InvokeAsync(context, Options.Create(options), new FixedClock(Now));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private static DefaultHttpContext Context(string method, string path)
    {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            static _ => Task.CompletedTask,
            new EndpointMetadataCollection(FreshAuthenticationRequiredMetadata.Instance),
            "unit-test"));
        return context;
    }

    private sealed class FixedClock : IAuthClock
    {
        public FixedClock()
            : this(Now)
        {
        }

        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
