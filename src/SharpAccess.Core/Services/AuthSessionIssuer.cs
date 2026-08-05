using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using SharpAccess.Tokens;
using Microsoft.Extensions.Options;

namespace SharpAccess.Services;

internal interface IAuthSessionIssuer
{
    // Issues a signed access token and an optional refresh-token family for one active context.
    Task<ServiceResult<SessionTokens>> IssueSessionAsync(
        AuthUser user,
        Guid? tenantId,
        Guid? familyId,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    // Creates the authorization context after tenant membership has already been checked.
    Task<UserContext> BuildContextAsync(
        AuthUser user,
        Guid? tenantId,
        CancellationToken cancellationToken);

    // Creates a signed access token for an already validated user context.
    AccessTokenResult CreateAccessToken(UserContext context);

    // Creates a token while preserving the primary authentication time across refresh rotation.
    AccessTokenResult CreateAccessToken(UserContext context, DateTimeOffset authenticatedUtc) =>
        CreateAccessToken(context);

    // Creates an opaque refresh token and its hashed persistence record without saving it.
    (string RawToken, RefreshTokenRecord Record) CreateRefreshToken(
        AuthUser user,
        Guid familyId,
        RequestMetadata metadata,
        DateTimeOffset now);

    // Creates a replacement refresh token without making token rotation count as primary authentication.
    (string RawToken, RefreshTokenRecord Record) CreateRefreshToken(
        AuthUser user,
        Guid familyId,
        RequestMetadata metadata,
        DateTimeOffset now,
        DateTimeOffset authenticatedUtc) =>
        CreateRefreshToken(user, familyId, metadata, now);
}

internal sealed class AuthSessionIssuer(
    IAuthSessionStore store,
    IAccessTokenService accessTokens,
    ITokenProtector tokens,
    IAuthClock clock,
    IOptions<AuthOptions> options) : IAuthSessionIssuer
{
    private readonly AuthOptions _options = options.Value;

    // Issues a signed access token and an optional refresh-token family for one active context.
    public async Task<ServiceResult<SessionTokens>> IssueSessionAsync(
        AuthUser user,
        Guid? tenantId,
        Guid? familyId,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!user.IsActive || !user.EmailVerifiedUtc.HasValue)
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Unauthorized, "invalid_user");
        }

        DateTimeOffset authenticatedUtc = clock.UtcNow;
        UserContext context = await BuildContextAsync(user, tenantId, cancellationToken).ConfigureAwait(false);
        AccessTokenResult accessToken = accessTokens.Create(context, authenticatedUtc);
        if (!_options.Features.RefreshTokens)
        {
            return ServiceResult<SessionTokens>.Success(new SessionTokens(
                accessToken.Token,
                accessToken.ExpiresUtc,
                string.Empty,
                clock.UtcNow));
        }

        DateTimeOffset now = clock.UtcNow;
        (string rawRefreshToken, RefreshTokenRecord record) = CreateRefreshToken(
            user,
            familyId ?? Guid.NewGuid(),
            metadata,
            now,
            authenticatedUtc);
        bool created = await store.TryCreateRefreshTokenAsync(
            record,
            _options.SecurityLimits.MaximumActiveRefreshFamiliesPerUser,
            _options.SecurityLimits.MaximumActiveRefreshTokensPerFamily,
            cancellationToken).ConfigureAwait(false);
        if (!created)
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Conflict, "refresh_session_limit_exceeded");
        }

        return ServiceResult<SessionTokens>.Success(new SessionTokens(
            accessToken.Token,
            accessToken.ExpiresUtc,
            rawRefreshToken,
            record.ExpiresUtc));
    }

    // Creates the authorization context after tenant membership has already been checked.
    public async Task<UserContext> BuildContextAsync(
        AuthUser user,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        EffectiveAuthorizationContext authorization = await store.GetEffectiveAuthorizationContextAsync(
            user.Id,
            tenantId,
            cancellationToken).ConfigureAwait(false);
        if (authorization.AuthorizationVersion != user.SecurityVersion)
        {
            throw new InvalidOperationException("The provider returned an authorization version that does not match the active user.");
        }

        return new UserContext(
            user.Id,
            user.Email,
            user.EmailVerifiedUtc.HasValue,
            authorization,
            user.SecurityVersion);
    }

    // Creates a signed access token for an already validated user context.
    public AccessTokenResult CreateAccessToken(UserContext context) => accessTokens.Create(context, clock.UtcNow);

    public AccessTokenResult CreateAccessToken(UserContext context, DateTimeOffset authenticatedUtc) =>
        accessTokens.Create(context, authenticatedUtc);

    // Creates an opaque refresh token and its hashed persistence record without saving it.
    public (string RawToken, RefreshTokenRecord Record) CreateRefreshToken(
        AuthUser user,
        Guid familyId,
        RequestMetadata metadata,
        DateTimeOffset now) =>
        CreateRefreshToken(user, familyId, metadata, now, now);

    public (string RawToken, RefreshTokenRecord Record) CreateRefreshToken(
        AuthUser user,
        Guid familyId,
        RequestMetadata metadata,
        DateTimeOffset now,
        DateTimeOffset authenticatedUtc)
    {
        string rawRefreshToken = tokens.Generate();
        RefreshTokenRecord record = new(
            Guid.NewGuid(),
            user.Id,
            tokens.Hash(rawRefreshToken),
            familyId,
            user.SecurityVersion,
            metadata.IpAddress,
            metadata.UserAgent,
            authenticatedUtc,
            now,
            now.AddDays(_options.RefreshTokenDays),
            null,
            null);
        return (rawRefreshToken, record);
    }
}
