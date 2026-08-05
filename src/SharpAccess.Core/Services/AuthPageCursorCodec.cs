using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using SharpAccess.Persistence;

namespace SharpAccess.Services;

// Protects and validates versioned page cursors before provider code receives a query.
internal interface IAuthPageCursorCodec
{
    // Validates a public request and decodes its protected keyset position for one exact collection scope.
    bool TryCreateQuery(
        SharpAccessPageRequest request,
        string scope,
        Guid? scopeId,
        out AuthPageQuery query);

    // Creates a public page and protects its provider-supplied continuation position.
    SharpAccessPage<T> CreatePage<T>(
        AuthPageSlice<T> slice,
        string scope,
        Guid? scopeId);
}

// Uses the host Data Protection key ring to make cursor contents opaque and tamper-evident.
internal sealed class AuthPageCursorCodec : IAuthPageCursorCodec
{
    internal const int CurrentVersion = 1;
    internal const int MaximumCursorLength = 2_048;
    internal const string UsersScope = "users";
    internal const string AuditScope = "audit";
    internal const string RolesScope = "roles";
    internal const string PermissionsScope = "permissions";
    internal const string TenantsScope = "tenants";
    internal const string TenantMembersScope = "tenant-members";
    private const string CursorPrefix = "v1.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly IDataProtector _protector;

    // Creates a stable-purpose protector whose payload also carries collection isolation fields.
    public AuthPageCursorCodec(IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _protector = dataProtectionProvider.CreateProtector("SharpAccess.Pagination.v1");
    }

    // Validates a public request and decodes its protected keyset position for one exact collection scope.
    public bool TryCreateQuery(
        SharpAccessPageRequest request,
        string scope,
        Guid? scopeId,
        out AuthPageQuery query)
    {
        query = new AuthPageQuery(SharpAccessPageRequest.DefaultLimit, null);
        if (!IsValidRequest(request, scope))
        {
            return false;
        }

        if (string.IsNullOrEmpty(request.Cursor))
        {
            query = new AuthPageQuery(request.Limit, null);
            return true;
        }

        return IsValidCursorEnvelope(request.Cursor)
            && TryDecodeQuery(request.Cursor, request.Limit, scope, scopeId, out query);
    }

    // Creates a public page and protects its provider-supplied continuation position.
    public SharpAccessPage<T> CreatePage<T>(
        AuthPageSlice<T> slice,
        string scope,
        Guid? scopeId)
    {
        ArgumentNullException.ThrowIfNull(slice);
        string? cursor = slice.Next is null
            ? null
            : Protect(scope, scopeId, slice.Next);
        return new SharpAccessPage<T>(slice.Items, cursor);
    }

    private static bool IsValidRequest(SharpAccessPageRequest? request, string scope) =>
        request is not null
        && request.Limit >= 1
        && request.Limit <= SharpAccessPageRequest.MaximumLimit
        && !string.IsNullOrWhiteSpace(scope);

    private static bool IsValidCursorEnvelope(string cursor) =>
        cursor.Length <= MaximumCursorLength
        && !string.IsNullOrWhiteSpace(cursor)
        && cursor.StartsWith(CursorPrefix, StringComparison.Ordinal);

    private bool TryDecodeQuery(
        string cursor,
        int limit,
        string scope,
        Guid? scopeId,
        out AuthPageQuery query)
    {
        query = new AuthPageQuery(SharpAccessPageRequest.DefaultLimit, null);
        try
        {
            string json = _protector.Unprotect(cursor[CursorPrefix.Length..]);
            CursorPayload? payload = JsonSerializer.Deserialize<CursorPayload>(json, SerializerOptions);
            return TryCreateQueryFromPayload(payload, limit, scope, scopeId, out query);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryCreateQueryFromPayload(
        CursorPayload? payload,
        int limit,
        string scope,
        Guid? scopeId,
        out AuthPageQuery query)
    {
        query = new AuthPageQuery(SharpAccessPageRequest.DefaultLimit, null);
        if (!IsPayloadBoundToScope(payload, scope, scopeId)
            || !TryCreateBoundary(payload!, out AuthPageBoundary? boundary))
        {
            return false;
        }

        query = new AuthPageQuery(limit, boundary);
        return true;
    }

    private static bool IsPayloadBoundToScope(
        CursorPayload? payload,
        string scope,
        Guid? scopeId) =>
        payload is not null
        && payload.Version == CurrentVersion
        && string.Equals(payload.Scope, scope, StringComparison.Ordinal)
        && ScopeMatches(payload.ScopeId, scopeId);

    private static bool TryCreateBoundary(CursorPayload payload, out AuthPageBoundary? boundary)
    {
        boundary = null;
        if (!DateTimeOffset.TryParseExact(
                payload.CreatedUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset createdUtc)
            || !Guid.TryParseExact(payload.Id, "D", out Guid id)
            || id == Guid.Empty)
        {
            return false;
        }

        boundary = new AuthPageBoundary(createdUtc, id);
        return true;
    }

    // Protects one strictly shaped cursor payload using the configured key ring.
    private string Protect(string scope, Guid? scopeId, AuthPageBoundary boundary)
    {
        CursorPayload payload = new(
            CurrentVersion,
            scope,
            scopeId?.ToString("D"),
            boundary.CreatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            boundary.Id.ToString("D"));
        return CursorPrefix + _protector.Protect(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    // Compares the protected isolation identifier with the current authorized collection scope.
    private static bool ScopeMatches(string? encodedScopeId, Guid? expectedScopeId)
    {
        if (!expectedScopeId.HasValue)
        {
            return encodedScopeId is null;
        }

        return Guid.TryParseExact(encodedScopeId, "D", out Guid parsed)
            && parsed == expectedScopeId.Value;
    }

    private sealed record CursorPayload(
        int Version,
        string Scope,
        string? ScopeId,
        string CreatedUtc,
        string Id);
}
