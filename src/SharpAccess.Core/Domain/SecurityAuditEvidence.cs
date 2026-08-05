namespace SharpAccess.Domain;

// Creates bounded, provider-neutral audit evidence before a security mutation starts.
internal static class SecurityAuditEvidence
{
    // Creates provider-contract evidence when a caller has no request metadata.
    internal static AuditRecord ForStoreMutation(
        DateTimeOffset createdUtc,
        string eventType,
        Guid? userId = null,
        Guid? tenantId = null) =>
        Create(createdUtc, eventType, userId, tenantId, null, null, "source=store_contract");

    // Creates one complete outcome bundle for provider-level rotation calls.
    internal static RefreshTokenAuditEvidence ForRefreshRotation(
        DateTimeOffset createdUtc,
        Guid? userId = null,
        Guid? tenantId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? familyDetail = null) =>
        new(
            Create(createdUtc, "refresh_token_rotated", userId, tenantId, ipAddress, userAgent, familyDetail),
            Create(createdUtc, "refresh_token_reuse_detected", userId, tenantId, ipAddress, userAgent, familyDetail),
            Create(createdUtc, "refresh_token_family_revoked", userId, tenantId, ipAddress, userAgent, JoinDetail(familyDetail, "reason=user_invalid")),
            Create(createdUtc, "refresh_token_expired", userId, tenantId, ipAddress, userAgent, familyDetail),
            Create(createdUtc, "refresh_token_family_revoked", userId, tenantId, ipAddress, userAgent, JoinDetail(familyDetail, "reason=family_limit")));

    // Creates one sanitized audit row with a caller-supplied identifier for deterministic rollback testing.
    internal static AuditRecord Create(
        Guid id,
        DateTimeOffset createdUtc,
        string eventType,
        Guid? userId,
        Guid? tenantId,
        string? ipAddress,
        string? userAgent,
        string? detail)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return new AuditRecord(
            id,
            createdUtc,
            Bound(eventType, 128)!,
            userId,
            tenantId,
            Bound(ipAddress, 64),
            Bound(userAgent, 512),
            Bound(detail, 1_024));
    }

    // Creates one sanitized audit row with a cryptographically unpredictable identifier.
    internal static AuditRecord Create(
        DateTimeOffset createdUtc,
        string eventType,
        Guid? userId,
        Guid? tenantId,
        string? ipAddress,
        string? userAgent,
        string? detail) =>
        Create(Guid.NewGuid(), createdUtc, eventType, userId, tenantId, ipAddress, userAgent, detail);

    // Bounds untrusted metadata without splitting a UTF-16 surrogate pair.
    private static string? Bound(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length <= maximumLength)
        {
            return trimmed;
        }

        int length = maximumLength;
        if (char.IsHighSurrogate(trimmed[length - 1]))
        {
            length--;
        }

        return trimmed[..length];
    }

    // Joins trusted bounded detail fragments without exposing raw token material.
    private static string JoinDetail(string? first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : $"{first};{second}";
}

// Supplies the single canonical row selected by a transaction for each mutating rotation outcome.
internal sealed record RefreshTokenAuditEvidence(
    AuditRecord Rotated,
    AuditRecord Reused,
    AuditRecord UserInvalid,
    AuditRecord Expired,
    AuditRecord LimitExceeded)
{
    // Selects exactly one row for an outcome that changed refresh-token state.
    internal AuditRecord For(TokenRotationStatus status) => status switch
    {
        TokenRotationStatus.Success => Rotated,
        TokenRotationStatus.Reused => Reused,
        TokenRotationStatus.UserInvalid => UserInvalid,
        TokenRotationStatus.Expired => Expired,
        TokenRotationStatus.LimitExceeded => LimitExceeded,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The outcome did not mutate refresh-token state.")
    };
}
