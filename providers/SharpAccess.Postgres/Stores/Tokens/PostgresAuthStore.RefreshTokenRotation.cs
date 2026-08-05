using SharpAccess.Domain;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    private static async Task<TokenRotationStatus?> GetRefreshRotationRejectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RefreshTokenRecord existing,
        RefreshTokenRecord replacement,
        DateTimeOffset now,
        int maximumActiveTokensPerFamily,
        CancellationToken cancellationToken)
    {
        if (existing.RevokedUtc.HasValue)
        {
            return TokenRotationStatus.Reused;
        }

        if (existing.ExpiresUtc <= now)
        {
            return TokenRotationStatus.Expired;
        }

        AuthUser? user = await FindUserInternalAsync(
            connection,
            transaction,
            existing.UserId,
            cancellationToken).ConfigureAwait(false);

        if (IsInvalidRefreshRotation(user, existing, replacement))
        {
            return TokenRotationStatus.UserInvalid;
        }

        int activeTokens = await CountActiveRefreshTokensInFamilyForLimitAsync(
            connection,
            transaction,
            existing.FamilyId,
            now,
            cancellationToken).ConfigureAwait(false);

        return activeTokens > maximumActiveTokensPerFamily
            ? TokenRotationStatus.LimitExceeded
            : null;
    }

    private static bool IsInvalidRefreshRotation(
        AuthUser? user,
        RefreshTokenRecord existing,
        RefreshTokenRecord replacement) =>
        user is null
        || !user.IsActive
        || !user.EmailVerifiedUtc.HasValue
        || user.SecurityVersion != existing.SecurityVersion
        || replacement.UserId != existing.UserId
        || replacement.FamilyId != existing.FamilyId
        || replacement.SecurityVersion != user.SecurityVersion;

    private static async Task<TokenRotationResult> CompleteRejectedRefreshRotationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RefreshTokenRecord existing,
        DateTimeOffset now,
        RefreshTokenAuditEvidence audit,
        TokenRotationStatus status,
        CancellationToken cancellationToken)
    {
        switch (status)
        {
            case TokenRotationStatus.Expired:
                await RevokeSelectedRefreshTokenAsync(
                    connection,
                    transaction,
                    existing,
                    now,
                    cancellationToken).ConfigureAwait(false);
                break;

            case TokenRotationStatus.Reused:
            case TokenRotationStatus.UserInvalid:
            case TokenRotationStatus.LimitExceeded:
                await RevokeFamilyInternalAsync(
                    connection,
                    transaction,
                    existing.FamilyId,
                    now,
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported rejected refresh-token outcome: {status}");
        }

        return await CommitRefreshRotationOutcomeAsync(
            connection,
            transaction,
            existing,
            audit,
            status,
            cancellationToken).ConfigureAwait(false);
    }

    private static Task<int> RevokeSelectedRefreshTokenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RefreshTokenRecord existing,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE auth_refresh_tokens
            SET revoked_utc=@now
            WHERE id=@id AND revoked_utc IS NULL;
            """,
            cancellationToken,
            ("@now", ToUtc(now)),
            ("@id", existing.Id));

    private static Task<int> MarkRefreshTokenReplacedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RefreshTokenRecord existing,
        RefreshTokenRecord replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE auth_refresh_tokens
            SET revoked_utc=@now,replaced_by_token_id=@replacementId
            WHERE id=@id AND revoked_utc IS NULL;
            """,
            cancellationToken,
            ("@now", ToUtc(now)),
            ("@replacementId", replacement.Id),
            ("@id", existing.Id));

    private static async Task<TokenRotationResult> CommitRefreshRotationOutcomeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RefreshTokenRecord existing,
        RefreshTokenAuditEvidence audit,
        TokenRotationStatus status,
        CancellationToken cancellationToken)
    {
        await InsertAuditAsync(
            connection,
            transaction,
            EnrichRotationAudit(audit.For(status), existing),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new TokenRotationResult(
            status,
            existing.UserId,
            existing.FamilyId);
    }
}
