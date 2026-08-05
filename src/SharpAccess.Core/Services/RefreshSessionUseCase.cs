using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using SharpAccess.Tokens;
using Microsoft.Extensions.Options;

namespace SharpAccess.Services;

internal sealed class RefreshSessionUseCase(
    IAuthRefreshSessionStore store,
    ITokenProtector tokens,
    IAuthClock clock,
    IAuthSessionIssuer sessions,
    IOptions<AuthOptions> options) : IRefreshSessionUseCase
{
    private readonly AuthOptions _options = options.Value;

    // Prepares a complete replacement response before atomically rotating the refresh token.
    public async Task<ServiceResult<SessionTokens>> RefreshAsync(
        string? refreshToken,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (IsInvalidRefreshRequest(refreshToken))
        {
            return InvalidRefreshToken();
        }

        (string? existingHash, RefreshTokenRecord? existing) = await FindRefreshTokenAsync(
            refreshToken!,
            cancellationToken).ConfigureAwait(false);
        if (existing is null || existingHash is null)
        {
            return InvalidRefreshToken();
        }

        DateTimeOffset now = clock.UtcNow;
        if (existing.RevokedUtc.HasValue)
        {
            return await HandleReplayAsync(
                existingHash,
                existing,
                tenantId,
                metadata,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        AuthUser? user = await store.FindUserByIdAsync(existing.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return InvalidRefreshToken();
        }

        if (!await HasTenantAccessAsync(user.Id, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Forbidden, "tenant_access_denied");
        }

        (SessionTokens response, RefreshTokenRecord replacement) = await PrepareReplacementAsync(
            user,
            existing,
            tenantId,
            metadata,
            now,
            cancellationToken).ConfigureAwait(false);
        TokenRotationResult rotation = await store.RotateRefreshTokenAsync(
            existingHash,
            replacement,
            now,
            _options.SecurityLimits.MaximumActiveRefreshTokensPerFamily,
            CreateRotationEvidence(existing, tenantId, metadata, now),
            cancellationToken).ConfigureAwait(false);

        return MapRotationResult(rotation.Status, response);
    }

    // Revokes the caller's selected refresh token while preserving idempotent logout behavior.
    public async Task<ServiceResult<bool>> LogoutAsync(
        Guid userId,
        string? refreshToken,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Features.RefreshTokens)
        {
            return ServiceResult<bool>.Failure(AuthError.Disabled, "refresh_tokens_disabled");
        }

        if (userId == Guid.Empty)
        {
            return ServiceResult<bool>.Failure(AuthError.Unauthorized, "invalid_user");
        }

        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 1_024)
        {
            return ServiceResult<bool>.Success(true);
        }

        DateTimeOffset now = clock.UtcNow;
        _ = await RevokeCandidateAsync(
            refreshToken,
            userId,
            allowAnyUser: false,
            revokeFamily: false,
            now,
            SecurityAuditEvidence.Create(
                now,
                "logout_success",
                userId,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                null),
            cancellationToken).ConfigureAwait(false);

        return ServiceResult<bool>.Success(true);
    }

    // Revokes a selected token or family after provider-side ownership validation.
    public async Task<ServiceResult<bool>> RevokeAsync(
        Guid userId,
        bool canManageSessions,
        string? refreshToken,
        bool revokeFamily,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Features.RefreshTokens)
        {
            return ServiceResult<bool>.Failure(AuthError.Disabled, "refresh_tokens_disabled");
        }

        if (userId == Guid.Empty)
        {
            return ServiceResult<bool>.Failure(AuthError.Unauthorized, "invalid_user");
        }

        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 1_024)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_revoke_request");
        }

        DateTimeOffset now = clock.UtcNow;
        bool revoked = await RevokeCandidateAsync(
            refreshToken,
            userId,
            canManageSessions,
            revokeFamily,
            now,
            SecurityAuditEvidence.Create(
                now,
                revokeFamily ? "refresh_token_family_revoked" : "refresh_token_revoked",
                userId,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                null),
            cancellationToken).ConfigureAwait(false);
        if (!revoked)
        {
            return ServiceResult<bool>.Failure(AuthError.NotFound, "session_not_found");
        }

        return ServiceResult<bool>.Success(true);
    }

    private bool IsInvalidRefreshRequest(string? refreshToken) =>
        !_options.Features.RefreshTokens
        || string.IsNullOrWhiteSpace(refreshToken)
        || refreshToken.Length > 1_024;

    private async Task<ServiceResult<SessionTokens>> HandleReplayAsync(
        string existingHash,
        RefreshTokenRecord existing,
        Guid? tenantId,
        RequestMetadata metadata,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string replayDetail = tenantId.HasValue
            ? $"family={existing.FamilyId:D};requested_tenant={tenantId.Value:D}"
            : $"family={existing.FamilyId:D}";
        _ = await store.HandleRefreshTokenReplayAsync(
            existingHash,
            now,
            SecurityAuditEvidence.Create(
                now,
                "refresh_token_reuse_detected",
                existing.UserId,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                replayDetail),
            cancellationToken).ConfigureAwait(false);
        return InvalidRefreshToken();
    }

    private async Task<bool> HasTenantAccessAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            return true;
        }

        return _options.Features.Tenancy
            && await store.IsTenantMemberAsync(userId, tenantId.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(SessionTokens Response, RefreshTokenRecord Replacement)> PrepareReplacementAsync(
        AuthUser user,
        RefreshTokenRecord existing,
        Guid? tenantId,
        RequestMetadata metadata,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        UserContext context = await sessions.BuildContextAsync(
            user,
            tenantId,
            cancellationToken).ConfigureAwait(false);
        AccessTokenResult accessToken = sessions.CreateAccessToken(context, existing.AuthenticatedUtc);
        (string rawReplacement, RefreshTokenRecord replacement) = sessions.CreateRefreshToken(
            user,
            existing.FamilyId,
            metadata,
            now,
            existing.AuthenticatedUtc);
        SessionTokens response = new(
            accessToken.Token,
            accessToken.ExpiresUtc,
            rawReplacement,
            replacement.ExpiresUtc);
        return (response, replacement);
    }

    private static RefreshTokenAuditEvidence CreateRotationEvidence(
        RefreshTokenRecord existing,
        Guid? tenantId,
        RequestMetadata metadata,
        DateTimeOffset now) =>
        SecurityAuditEvidence.ForRefreshRotation(
            now,
            existing.UserId,
            tenantId,
            metadata.IpAddress,
            metadata.UserAgent,
            $"family={existing.FamilyId:D}");

    private static ServiceResult<SessionTokens> MapRotationResult(
        TokenRotationStatus status,
        SessionTokens response) =>
        status switch
        {
            TokenRotationStatus.Success => ServiceResult<SessionTokens>.Success(response),
            TokenRotationStatus.LimitExceeded => ServiceResult<SessionTokens>.Failure(
                AuthError.Conflict,
                "refresh_session_limit_exceeded"),
            _ => InvalidRefreshToken()
        };

    private static ServiceResult<SessionTokens> InvalidRefreshToken() =>
        ServiceResult<SessionTokens>.Failure(AuthError.Unauthorized, "invalid_refresh_token");

    // Locates a persisted refresh token across the accepted keyed-hash versions.
    private async Task<(string? Hash, RefreshTokenRecord? Token)> FindRefreshTokenAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        foreach (string hash in tokens.HashCandidates(rawToken))
        {
            RefreshTokenRecord? candidate = await store.FindRefreshTokenByHashAsync(
                hash,
                cancellationToken).ConfigureAwait(false);
            if (candidate is not null)
            {
                return (hash, candidate);
            }
        }

        return (null, null);
    }

    // Attempts a revocation across accepted keyed-hash versions using one audit identifier.
    private async Task<bool> RevokeCandidateAsync(
        string rawToken,
        Guid requestingUserId,
        bool allowAnyUser,
        bool revokeFamily,
        DateTimeOffset now,
        AuditRecord auditEvidence,
        CancellationToken cancellationToken)
    {
        foreach (string hash in tokens.HashCandidates(rawToken))
        {
            if (await store.RevokeRefreshTokenAsync(
                    hash,
                    requestingUserId,
                    allowAnyUser,
                    revokeFamily,
                    now,
                    auditEvidence,
                    cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}
