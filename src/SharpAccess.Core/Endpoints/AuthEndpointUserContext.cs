using System.Security.Claims;
using SharpAccess.Configuration;

namespace SharpAccess.Endpoints;

// Reads authenticated user and tenant identifiers from endpoint principals.
internal static class AuthEndpointUserContext
{
    // Extracts the authenticated user identifier from the subject claim.
    internal static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out userId);

    // Extracts the active tenant identifier when present.
    internal static Guid? TryGetTenantId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(AuthConstants.TenantClaim), out Guid tenantId)
            ? tenantId
            : null;
}
