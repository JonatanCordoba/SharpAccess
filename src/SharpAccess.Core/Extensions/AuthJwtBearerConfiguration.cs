using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using SharpAccess;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.DependencyInjection;

internal static class AuthJwtBearerConfiguration
{
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The direct internal overload is retained for tests; the resolver closure owns the configured key ring for the JwtBearerOptions lifetime.")]
    // Configures JWT validation with a clock-aware key ring owned by the options lifetime.
    internal static void ConfigureJwtBearer(
        JwtBearerOptions bearer,
        IOptions<AuthOptions> configuredOptions,
        IAuthClock clock)
    {
        ConfiguredAccessTokenSigningKeyRing keyRing = new(configuredOptions, clock);
        ConfigureJwtBearer(bearer, configuredOptions, keyRing, clock);
    }

    // Configures strict JWT validation against the supplied key ring and project clock.
    internal static void ConfigureJwtBearer(
        JwtBearerOptions bearer,
        IOptions<AuthOptions> configuredOptions,
        IAccessTokenSigningKeyRing keyRing,
        IAuthClock clock)
    {
        AuthOptions options = configuredOptions.Value;
        AccessTokenKeyRingGuard.Validate(keyRing, clock.UtcNow);
        string[] algorithms = keyRing.VerificationKeys
            .Select(static key => key.Algorithm)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        bearer.MapInboundClaims = false;
        bearer.RequireHttpsMetadata = options.BaseUri.Scheme == Uri.UriSchemeHttps;
        bearer.SaveToken = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.JwtIssuer,
            ValidateAudience = true,
            RequireAudience = true,
            ValidAudience = options.JwtAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = algorithms,
            TryAllIssuerSigningKeys = false,
            IssuerSigningKeyResolver = (_, _, keyId, _) => ResolveKeys(keyRing, keyId, clock.UtcNow),
            AlgorithmValidator = (algorithm, securityKey, _, _) => ValidateAlgorithm(
                keyRing,
                algorithm,
                securityKey,
                clock.UtcNow),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = AuthConstants.GlobalRoleClaim
        };
        bearer.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidatePersistedUserAsync,
            OnChallenge = AuthProblemDetailsWriter.WriteChallengeAsync,
            OnForbidden = AuthProblemDetailsWriter.WriteForbiddenAsync
        };
    }

    // Resolves exactly one currently accepted verification key by key identifier.
    private static SecurityKey[] ResolveKeys(
        IAccessTokenSigningKeyRing keyRing,
        string? keyId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return Array.Empty<SecurityKey>();
        }

        AccessTokenVerificationKey? key = keyRing.VerificationKeys.SingleOrDefault(
            candidate => string.Equals(candidate.KeyId, keyId, StringComparison.Ordinal));
        return key is not null && AccessTokenKeyRingGuard.IsAccepted(key, now)
            ? new[] { key.VerificationKey }
            : Array.Empty<SecurityKey>();
    }

    // Requires the token algorithm to match the currently accepted configured key.
    private static bool ValidateAlgorithm(
        IAccessTokenSigningKeyRing keyRing,
        string algorithm,
        SecurityKey securityKey,
        DateTimeOffset now)
    {
        AccessTokenVerificationKey? key = keyRing.VerificationKeys.SingleOrDefault(
            candidate => ReferenceEquals(candidate.VerificationKey, securityKey)
                || (!string.IsNullOrWhiteSpace(securityKey.KeyId)
                    && string.Equals(candidate.KeyId, securityKey.KeyId, StringComparison.Ordinal)));
        return key is not null
            && string.Equals(key.Algorithm, algorithm, StringComparison.Ordinal)
            && AccessTokenKeyRingGuard.IsAccepted(key, now);
    }

    // Revalidates account, authorization version, and active tenant state after JWT validation.
    private static async Task ValidatePersistedUserAsync(TokenValidatedContext context)
    {
        if (!TryReadIdentityClaims(
                context.Principal,
                out Guid userId,
                out int securityVersion,
                out long authorizationVersion))
        {
            context.Fail("Invalid identity claims.");
            return;
        }

        IAuthUserTenantStore store = context.HttpContext.RequestServices.GetRequiredService<IAuthUserTenantStore>();
        AuthUser? user = await store.FindUserByIdAsync(
            userId,
            context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (!IsPersistedIdentityValid(user, securityVersion, authorizationVersion))
        {
            context.Fail("The account or authorization context is no longer valid.");
            return;
        }

        string? tenantValue = context.Principal?.FindFirstValue(AuthConstants.TenantClaim);
        if (!await IsTenantContextValidAsync(
                store,
                userId,
                tenantValue,
                context.HttpContext.RequestAborted).ConfigureAwait(false))
        {
            context.Fail("The tenant context is no longer valid.");
            return;
        }

        string? ownerTenantValue = context.Principal?.FindFirstValue(AuthConstants.TenantOwnerClaim);
        if (!IsOwnerContextValid(ownerTenantValue, tenantValue))
        {
            context.Fail("The tenant owner context is invalid.");
        }
    }

    private static bool TryReadIdentityClaims(
        ClaimsPrincipal? principal,
        out Guid userId,
        out int securityVersion,
        out long authorizationVersion)
    {
        userId = Guid.Empty;
        securityVersion = 0;
        authorizationVersion = 0;
        return Guid.TryParse(principal?.FindFirstValue("sub"), out userId)
            && int.TryParse(
                principal?.FindFirstValue(AuthConstants.SecurityVersionClaim),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out securityVersion)
            && long.TryParse(
                principal?.FindFirstValue(AuthConstants.AuthorizationVersionClaim),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out authorizationVersion);
    }

    private static bool IsPersistedIdentityValid(
        AuthUser? user,
        int securityVersion,
        long authorizationVersion) =>
        user is not null
        && user.IsActive
        && user.EmailVerifiedUtc.HasValue
        && user.SecurityVersion == securityVersion
        && user.SecurityVersion == authorizationVersion;

    private static async Task<bool> IsTenantContextValidAsync(
        IAuthUserTenantStore store,
        Guid userId,
        string? tenantValue,
        CancellationToken cancellationToken)
    {
        if (tenantValue is null)
        {
            return true;
        }

        return Guid.TryParse(tenantValue, out Guid tenantId)
            && await store.IsTenantMemberAsync(userId, tenantId, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsOwnerContextValid(string? ownerTenantValue, string? tenantValue)
    {
        if (ownerTenantValue is null)
        {
            return true;
        }

        return Guid.TryParse(ownerTenantValue, out Guid ownerTenantId)
            && Guid.TryParse(tenantValue, out Guid activeTenantId)
            && ownerTenantId == activeTenantId;
    }
}
