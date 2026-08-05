using SharpAccess.Domain;
using SharpAccess.Services;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    // Persists one protected, expiring OAuth state record.
    public async Task SaveOAuthStateAsync(OAuthStateRecord state, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            null,
            """
            INSERT INTO auth_oauth_states(
                id,provider,state_hash,hash_key_version,protected_code_verifier,return_url,created_utc,expires_utc,consumed_utc)
            VALUES(
                $id,$provider,$stateHash,substr($stateHash,1,8),$protected,$returnUrl,$created,$expires,NULL);
            """,
            cancellationToken,
            ("$id", state.Id.ToString("D")),
            ("$provider", state.Provider),
            ("$stateHash", state.StateHash),
            ("$protected", state.ProtectedCodeVerifier),
            ("$returnUrl", state.ReturnUrl),
            ("$created", Format(state.CreatedUtc)),
            ("$expires", Format(state.ExpiresUtc))).ConfigureAwait(false);
    }

    // Atomically consumes one matching, unexpired OAuth state.
    public async Task<OAuthStateRecord?> ConsumeOAuthStateAsync(
        string provider,
        string stateHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            OAuthStateRecord? state = null;
            await using (SqliteCommand command = CreateCommand(
                connection,
                transaction,
                """
                SELECT id,provider,state_hash,protected_code_verifier,return_url,created_utc,expires_utc,consumed_utc
                FROM auth_oauth_states
                WHERE provider=$provider AND state_hash=$stateHash
                  AND consumed_utc IS NULL AND expires_utc>$now
                LIMIT 1;
                """,
                ("$provider", provider),
                ("$stateHash", stateHash),
                ("$now", Format(now))))
            {
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    state = new OAuthStateRecord(
                        ParseGuid(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        ParseDate(reader.GetString(5)),
                        ParseDate(reader.GetString(6)),
                        ReadNullableDate(reader, 7));
                }
            }

            if (state is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            int consumed = await ExecuteAsync(
                connection,
                transaction,
                "UPDATE auth_oauth_states SET consumed_utc=$now WHERE id=$id AND consumed_utc IS NULL;",
                cancellationToken,
                ("$now", Format(now)),
                ("$id", state.Id.ToString("D"))).ConfigureAwait(false);
            if (consumed != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return state with { ConsumedUtc = now };
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Creates provider-contract audit evidence for direct external-account resolution calls.
    public Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(
        string provider,
        string providerSubject,
        string email,
        string normalizedEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        ResolveOAuthUserAsync(
            provider,
            providerSubject,
            email,
            normalizedEmail,
            now,
            SecurityAuditEvidence.Create(now, "oauth_account_linked", null, null, null, null, $"provider={provider}"),
            cancellationToken);

    // Resolves a provider account, safely links a verified matching email, or creates a verified external-only user.
    public async Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(
        string provider,
        string providerSubject,
        string email,
        string normalizedEmail,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        bool auditWriteStarted = false;
        try
        {
            AuthUser? linked = await FindOAuthUserInternalAsync(
                connection,
                transaction,
                provider,
                providerSubject,
                cancellationToken).ConfigureAwait(false);
            if (linked is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return linked.IsActive
                    ? ServiceResult<AuthUser>.Success(linked)
                    : ServiceResult<AuthUser>.Failure(AuthError.Unauthorized, "oauth_user_inactive");
            }

            AuthUser? existing = await FindUserByNormalizedEmailInternalAsync(
                connection,
                transaction,
                normalizedEmail,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null && (!existing.EmailVerifiedUtc.HasValue || !existing.IsActive))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return ServiceResult<AuthUser>.Failure(AuthError.Conflict, "oauth_account_conflict");
            }

            AuthUser user;
            if (existing is null)
            {
                user = new AuthUser(
                    Guid.NewGuid(),
                    email,
                    normalizedEmail,
                    null,
                    now,
                    true,
                    0,
                    null,
                    1,
                    now,
                    now);
                await InsertUserAsync(connection, transaction, user, cancellationToken).ConfigureAwait(false);
                await AssignRoleInternalAsync(
                    connection,
                    transaction,
                    user.Id,
                    Guid.Parse(UserRoleId),
                    null,
                    now,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                user = existing;
            }

            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO auth_oauth_accounts(id,user_id,provider,provider_subject,created_utc)
                VALUES($id,$userId,$provider,$subject,$created);
                """,
                cancellationToken,
                ("$id", Guid.NewGuid().ToString("D")),
                ("$userId", user.Id.ToString("D")),
                ("$provider", provider),
                ("$subject", providerSubject),
                ("$created", Format(now))).ConfigureAwait(false);
            auditWriteStarted = true;
            await InsertAuditAsync(connection, transaction, audit with { UserId = user.Id }, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResult<AuthUser>.Success(user);
        }
        catch (SqliteException exception) when (!auditWriteStarted && IsConstraintViolation(exception))
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            return ServiceResult<AuthUser>.Failure(AuthError.Conflict, "oauth_account_conflict");
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }
}
