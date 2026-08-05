using Microsoft.Data.Sqlite;
using SharpAccess.Domain;
using System.Data.Common;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    // Applies the ordered refresh-token rotation outcomes inside the caller-owned transaction.
    private static async Task<TokenRotationResult> RotateRefreshTokenCoreAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string existingTokenHash,
        RefreshTokenRecord replacement,
        DateTimeOffset now,
        int maximumActiveTokensPerFamily,
        RefreshTokenAuditEvidence audit,
        CancellationToken cancellationToken)
    {
        RefreshTokenRecord? existing = await FindRefreshTokenInternalAsync(
            connection,
            transaction,
            existingTokenHash,
            cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new TokenRotationResult(TokenRotationStatus.NotFound);
        }

        TokenRotationResult? existingOutcome = await TryCompleteExistingTokenOutcomeAsync(
            connection,
            transaction,
            existing,
            now,
            audit,
            cancellationToken).ConfigureAwait(false);
        if (existingOutcome is not null)
        {
            return existingOutcome;
        }

        AuthUser? user = await FindUserInternalAsync(
            connection,
            transaction,
            existing.UserId,
            cancellationToken).ConfigureAwait(false);
        if (!IsValidRotationRequest(user, existing, replacement))
        {
            return await CompleteFamilyRotationOutcomeAsync(
                connection,
                transaction,
                existing,
                now,
                TokenRotationStatus.UserInvalid,
                audit,
                cancellationToken).ConfigureAwait(false);
        }

        bool limitExceeded = await IsActiveTokenLimitExceededAsync(
            connection,
            transaction,
            existing.FamilyId,
            now,
            maximumActiveTokensPerFamily,
            cancellationToken).ConfigureAwait(false);
        if (limitExceeded)
        {
            return await CompleteFamilyRotationOutcomeAsync(
                connection,
                transaction,
                existing,
                now,
                TokenRotationStatus.LimitExceeded,
                audit,
                cancellationToken).ConfigureAwait(false);
        }

        return await CompleteSuccessfulRotationAsync(
            connection,
            transaction,
            existing,
            replacement,
            now,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    // Completes reuse and expiry outcomes that can be determined without loading the user.
    private static async Task<TokenRotationResult?> TryCompleteExistingTokenOutcomeAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        RefreshTokenRecord existing,
        DateTimeOffset now,
        RefreshTokenAuditEvidence audit,
        CancellationToken cancellationToken)
    {
        if (existing.RevokedUtc.HasValue)
        {
            return await CompleteFamilyRotationOutcomeAsync(
                connection,
                transaction,
                existing,
                now,
                TokenRotationStatus.Reused,
                audit,
                cancellationToken).ConfigureAwait(false);
        }

        if (existing.ExpiresUtc <= now)
        {
            return await CompleteExpiredRotationAsync(
                connection,
                transaction,
                existing,
                now,
                audit,
                cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    // Reports whether persisted user state and caller-provided replacement ownership are consistent.
    private static bool IsValidRotationRequest(
        AuthUser? user,
        RefreshTokenRecord existing,
        RefreshTokenRecord replacement) =>
        IsEligibleRotationUser(user, existing)
        && MatchesPersistedRotationOwnership(replacement, existing, user!);

    // Reports whether the persisted user may continue the selected refresh family.
    private static bool IsEligibleRotationUser(AuthUser? user, RefreshTokenRecord existing) =>
        user is not null
        && user.IsActive
        && user.EmailVerifiedUtc.HasValue
        && user.SecurityVersion == existing.SecurityVersion;

    // Reports whether the replacement preserves provider-trusted user, family, and security versions.
    private static bool MatchesPersistedRotationOwnership(
        RefreshTokenRecord replacement,
        RefreshTokenRecord existing,
        AuthUser user) =>
        replacement.UserId == existing.UserId
        && replacement.FamilyId == existing.FamilyId
        && replacement.SecurityVersion == user.SecurityVersion;

    // Reports whether one family already exceeds its configured active-token cap.
    private static async Task<bool> IsActiveTokenLimitExceededAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid familyId,
        DateTimeOffset now,
        int maximumActiveTokensPerFamily,
        CancellationToken cancellationToken)
    {
        int activeTokens = await CountActiveFamilyTokensAsync(
            connection,
            transaction,
            familyId,
            now,
            cancellationToken).ConfigureAwait(false);
        return activeTokens > maximumActiveTokensPerFamily;
    }

    // Revokes the selected expired token and commits its canonical outcome audit.
    private static async Task<TokenRotationResult> CompleteExpiredRotationAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        RefreshTokenRecord existing,
        DateTimeOffset now,
        RefreshTokenAuditEvidence audit,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE auth_refresh_tokens SET revoked_utc=$now WHERE id=$id AND revoked_utc IS NULL;",
            cancellationToken,
            ("$now", Format(now)),
            ("$id", existing.Id.ToString("D"))).ConfigureAwait(false);
        await InsertRotationAuditAsync(
            connection,
            transaction,
            existing,
            TokenRotationStatus.Expired,
            audit,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RotationResult(TokenRotationStatus.Expired, existing);
    }

    // Revokes the persisted family and commits the selected fail-closed outcome audit.
    private static async Task<TokenRotationResult> CompleteFamilyRotationOutcomeAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        RefreshTokenRecord existing,
        DateTimeOffset now,
        TokenRotationStatus status,
        RefreshTokenAuditEvidence audit,
        CancellationToken cancellationToken)
    {
        await RevokeFamilyInternalAsync(
            connection,
            transaction,
            existing.FamilyId,
            now,
            cancellationToken).ConfigureAwait(false);
        await InsertRotationAuditAsync(
            connection,
            transaction,
            existing,
            status,
            audit,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RotationResult(status, existing);
    }

    // Inserts the replacement, links the old row, and commits success or detected reuse atomically.
    private static async Task<TokenRotationResult> CompleteSuccessfulRotationAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        RefreshTokenRecord existing,
        RefreshTokenRecord replacement,
        DateTimeOffset now,
        RefreshTokenAuditEvidence audit,
        CancellationToken cancellationToken)
    {
        // Insert first so the self-referencing foreign key is valid when the old row points to its replacement.
        await InsertRefreshTokenAsync(connection, transaction, replacement, cancellationToken).ConfigureAwait(false);
        int revoked = await LinkReplacementAsync(
            connection,
            transaction,
            existing,
            replacement,
            now,
            cancellationToken).ConfigureAwait(false);
        if (revoked != 1)
        {
            return await CompleteFamilyRotationOutcomeAsync(
                connection,
                transaction,
                existing,
                now,
                TokenRotationStatus.Reused,
                audit,
                cancellationToken).ConfigureAwait(false);
        }

        await InsertRotationAuditAsync(
            connection,
            transaction,
            existing,
            TokenRotationStatus.Success,
            audit,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RotationResult(TokenRotationStatus.Success, existing);
    }

    // Links the persisted token to its replacement only when no concurrent revocation won the race.
    private static Task<int> LinkReplacementAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        RefreshTokenRecord existing,
        RefreshTokenRecord replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE auth_refresh_tokens
            SET revoked_utc=$now,replaced_by_token_id=$replacementId
            WHERE id=$id AND revoked_utc IS NULL;
            """,
            cancellationToken,
            ("$now", Format(now)),
            ("$replacementId", replacement.Id.ToString("D")),
            ("$id", existing.Id.ToString("D")));

    // Persists one outcome-specific rotation audit enriched with provider-trusted ownership.
    private static Task<int> InsertRotationAuditAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        RefreshTokenRecord existing,
        TokenRotationStatus status,
        RefreshTokenAuditEvidence audit,
        CancellationToken cancellationToken) =>
        InsertAuditAsync(
            connection,
            transaction,
            EnrichRotationAudit(audit.For(status), existing),
            cancellationToken);

    // Creates the canonical result for one persisted refresh-token family.
    private static TokenRotationResult RotationResult(
        TokenRotationStatus status,
        RefreshTokenRecord existing) =>
        new(status, existing.UserId, existing.FamilyId);
}
