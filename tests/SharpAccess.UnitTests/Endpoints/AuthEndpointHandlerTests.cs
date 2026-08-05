using System.Net;
using System.Security.Claims;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class AuthEndpointHandlerTests
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails()
        .BuildServiceProvider();

    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LogoutAndRevokeRejectMissingUsersBeforeCallingService()
    {
        FakeAuthService service = new();
        DefaultHttpContext context = CreateContext();

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.LogoutAsync(
                new LogoutRequest(null),
                context,
                service,
                Options.Create(TestOptions.Create()),
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.RevokeAsync(
                new RevokeRequest("opaque-value", RevokeFamily: false),
                context,
                service,
                CancellationToken.None)));
    }

    [Fact]
    public async Task MeMapsMissingUsersServiceFailuresAndSuccessResults()
    {
        FakeAuthService service = new();

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.MeAsync(CreateContext(), service, CancellationToken.None)));

        service.UserContextResult = ServiceResult<UserContext>.Failure(AuthError.NotFound, "user_not_found");
        Assert.Equal(StatusCodes.Status404NotFound, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.MeAsync(CreateContext(UserId, TenantId), service, CancellationToken.None)));
        Assert.Equal(TenantId, service.LastTenantId);

        service.UserContextResult = ServiceResult<UserContext>.Success(CreateUserContext());
        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.MeAsync(CreateContext(UserId, TenantId), service, CancellationToken.None)));
    }

    [Fact]
    public async Task PasswordAndVerificationHandlersMapSuccessAndFailureResults()
    {
        FakeAuthService service = new();
        DefaultHttpContext context = CreateContext(UserId);

        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.ChangePasswordAsync(
                new ChangePasswordRequest("CurrentValue123", "NextValue123"),
                CreateContext(),
                service,
                CancellationToken.None)));

        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.ChangePasswordAsync(
                new ChangePasswordRequest("CurrentValue123", "NextValue123"),
                context,
                service,
                CancellationToken.None)));
        Assert.Equal(UserId, service.LastUserId);

        service.BoolResult = ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_password_change");
        Assert.Equal(StatusCodes.Status400BadRequest, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.ChangePasswordAsync(
                new ChangePasswordRequest("CurrentValue123", "NextValue123"),
                context,
                service,
                CancellationToken.None)));

        service.MessageResult = ServiceResult<string>.Success("accepted");
        Assert.Equal(StatusCodes.Status202Accepted, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.ForgotPasswordAsync(
                new ForgotPasswordRequest("person@example.com"),
                context,
                service,
                CancellationToken.None)));

        service.BoolResult = ServiceResult<bool>.Success(true);
        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.ResetPasswordAsync(
                new ResetPasswordRequest("opaque-value", "NextValue123"),
                context,
                service,
                CancellationToken.None)));

        service.BoolResult = ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_reset_value");
        Assert.Equal(StatusCodes.Status400BadRequest, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.ResetPasswordAsync(
                new ResetPasswordRequest("opaque-value", "NextValue123"),
                context,
                service,
                CancellationToken.None)));

        service.BoolResult = ServiceResult<bool>.Success(true);
        Assert.Equal(StatusCodes.Status204NoContent, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.VerifyEmailAsync(
                new VerifyEmailRequest("opaque-value"),
                context,
                service,
                CancellationToken.None)));

        service.MessageResult = ServiceResult<string>.Success("accepted");
        Assert.Equal(StatusCodes.Status202Accepted, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.ResendVerificationAsync(
                new ResendVerificationRequest("person@example.com"),
                context,
                service,
                CancellationToken.None)));
    }

    private static DefaultHttpContext CreateContext(Guid? userId = null, Guid? tenantId = null)
    {
        DefaultHttpContext context = new()
        {
            RequestServices = Services
        };
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers.UserAgent = "unit-test";
        context.Request.Scheme = "https";

        List<Claim> claims = [];
        if (userId.HasValue)
        {
            claims.Add(new Claim("sub", userId.Value.ToString("D")));
        }

        if (tenantId.HasValue)
        {
            claims.Add(new Claim(AuthConstants.TenantClaim, tenantId.Value.ToString("D")));
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

    private static UserContext CreateUserContext() =>
        new(
            UserId,
            "person@example.com",
            true,
            new EffectiveAuthorizationContext(
                new GlobalAuthorizationContext(["User"], ["profile.read"]),
                new TenantAuthorizationContext(TenantId, false, [], []),
                AuthorizationVersion: 1),
            SecurityVersion: 1);

    private static SessionTokens Tokens() => new(
        "access-value",
        Now.AddMinutes(5),
        "refresh-value",
        Now.AddDays(1));

    private sealed class FakeAuthService : IAuthService
    {
        public Guid LastUserId { get; private set; }

        public Guid? LastTenantId { get; private set; }

        public string? LastRefreshValue { get; private set; }

        public ServiceResult<SessionTokens> SessionResult { get; set; } = ServiceResult<SessionTokens>.Success(Tokens());

        public ServiceResult<bool> BoolResult { get; set; } = ServiceResult<bool>.Success(true);

        public ServiceResult<string> MessageResult { get; set; } = ServiceResult<string>.Success("accepted");

        public ServiceResult<UserContext> UserContextResult { get; set; } = ServiceResult<UserContext>.Success(CreateUserContext());

        public Task<ServiceResult<string>> RegisterAsync(string? email, string? password, RequestMetadata metadata, CancellationToken cancellationToken = default) =>
            Task.FromResult(MessageResult);

        public Task<ServiceResult<SessionTokens>> LoginAsync(string? email, string? password, Guid? tenantId, RequestMetadata metadata, CancellationToken cancellationToken = default)
        {
            LastTenantId = tenantId;
            return Task.FromResult(SessionResult);
        }

        public Task<ServiceResult<SessionTokens>> RefreshAsync(string? refreshToken, Guid? tenantId, RequestMetadata metadata, CancellationToken cancellationToken = default)
        {
            LastRefreshValue = refreshToken;
            LastTenantId = tenantId;
            return Task.FromResult(SessionResult);
        }

        public Task<ServiceResult<bool>> LogoutAsync(Guid userId, string? refreshToken, RequestMetadata metadata, CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            LastRefreshValue = refreshToken;
            return Task.FromResult(BoolResult);
        }

        public Task<ServiceResult<bool>> RevokeAsync(Guid userId, bool canManageSessions, string? refreshToken, bool revokeFamily, RequestMetadata metadata, CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            LastRefreshValue = refreshToken;
            return Task.FromResult(BoolResult);
        }

        public Task<ServiceResult<UserContext>> GetMeAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            LastTenantId = tenantId;
            return Task.FromResult(UserContextResult);
        }

        public Task<ServiceResult<bool>> ChangePasswordAsync(Guid userId, string? currentPassword, string? newPassword, RequestMetadata metadata, CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            return Task.FromResult(BoolResult);
        }

        public Task<ServiceResult<string>> ForgotPasswordAsync(string? email, RequestMetadata metadata, CancellationToken cancellationToken = default) =>
            Task.FromResult(MessageResult);

        public Task<ServiceResult<bool>> ResetPasswordAsync(string? token, string? newPassword, RequestMetadata metadata, CancellationToken cancellationToken = default) =>
            Task.FromResult(BoolResult);

        public Task<ServiceResult<bool>> VerifyEmailAsync(string? token, RequestMetadata metadata, CancellationToken cancellationToken = default) =>
            Task.FromResult(BoolResult);

        public Task<ServiceResult<string>> ResendVerificationAsync(string? email, RequestMetadata metadata, CancellationToken cancellationToken = default) =>
            Task.FromResult(MessageResult);
    }
}
