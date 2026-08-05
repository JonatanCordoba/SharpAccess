using System.Globalization;
using System.Security.Claims;
using System.Text;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.Tokens;

internal interface IAccessTokenService
{
    // Creates a bounded signed access token preserving the primary authentication time.
    AccessTokenResult Create(UserContext user, DateTimeOffset authenticatedUtc);
}

internal sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresUtc);

internal sealed class JwtAccessTokenService : IAccessTokenService, IDisposable
{
    private readonly AuthOptions _options;
    private readonly IAuthClock _clock;
    private readonly IAccessTokenSigningKeyRing _keyRing;
    private readonly ConfiguredAccessTokenSigningKeyRing? _ownedKeyRing;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    // Creates the token service with a host- or container-provided signing key ring.
    public JwtAccessTokenService(
        Microsoft.Extensions.Options.IOptions<AuthOptions> options,
        IAuthClock clock,
        IAccessTokenSigningKeyRing keyRing)
    {
        _options = options.Value;
        _clock = clock;
        _keyRing = keyRing;
    }

    // Creates the test convenience service with a clock-aware configured key ring it owns.
    internal JwtAccessTokenService(
        Microsoft.Extensions.Options.IOptions<AuthOptions> options,
        IAuthClock clock)
    {
        _options = options.Value;
        _clock = clock;
        ConfiguredAccessTokenSigningKeyRing ring = new(options, clock);
        _keyRing = ring;
        _ownedKeyRing = ring;
    }

    // Creates an access token whose primary authentication time is the current project time.
    public AccessTokenResult Create(UserContext user)
        => Create(user, _clock.UtcNow);

    // Creates a bounded access token with explicit authentication and issuance times.
    public AccessTokenResult Create(UserContext user, DateTimeOffset authenticatedUtc)
    {
        ArgumentNullException.ThrowIfNull(user);
        DateTimeOffset issuedUtc = _clock.UtcNow;
        if (authenticatedUtc > issuedUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(authenticatedUtc),
                "The primary authentication time cannot be in the future.");
        }

        AccessTokenKeyRingGuard.Validate(_keyRing, issuedUtc);
        AccessTokenSigningKey active = _keyRing.ActiveSigningKey;
        DateTimeOffset expiresUtc = issuedUtc.AddMinutes(_options.AccessTokenMinutes);

        int roleCount = user.Authorization.Global.Roles.Count
            + (user.Authorization.Tenant?.Roles.Count ?? 0);
        int permissionCount = user.Authorization.Global.Permissions.Count
            + (user.Authorization.Tenant?.Permissions.Count ?? 0);
        if (roleCount > _options.SecurityLimits.MaximumRolesPerToken)
        {
            throw new InvalidOperationException("The authorization context exceeds MaximumRolesPerToken.");
        }

        if (permissionCount > _options.SecurityLimits.MaximumPermissionsPerToken)
        {
            throw new InvalidOperationException("The authorization context exceeds MaximumPermissionsPerToken.");
        }

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(
                JwtRegisteredClaimNames.Iat,
                issuedUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new(
                AuthConstants.AuthenticationTimeClaim,
                authenticatedUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new(
                AuthConstants.SecurityVersionClaim,
                user.SecurityVersion.ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer32),
            new(
                AuthConstants.AuthorizationVersionClaim,
                user.Authorization.AuthorizationVersion.ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        ];

        foreach (string role in user.Authorization.Global.Roles)
        {
            claims.Add(new Claim(AuthConstants.GlobalRoleClaim, role));
        }

        foreach (string permission in user.Authorization.Global.Permissions)
        {
            claims.Add(new Claim(AuthConstants.GlobalPermissionClaim, permission));
        }

        TenantAuthorizationContext? tenant = user.Authorization.Tenant;
        if (tenant is not null)
        {
            claims.Add(new Claim(AuthConstants.TenantClaim, tenant.TenantId.ToString("D")));
            if (tenant.IsOwner)
            {
                claims.Add(new Claim(AuthConstants.TenantOwnerClaim, tenant.TenantId.ToString("D")));
            }

            foreach (string role in tenant.Roles)
            {
                claims.Add(new Claim(AuthConstants.TenantRoleClaim, role));
            }

            foreach (string permission in tenant.Permissions)
            {
                claims.Add(new Claim(AuthConstants.TenantPermissionClaim, permission));
            }
        }

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.JwtIssuer,
            Audience = _options.JwtAudience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedUtc.UtcDateTime,
            NotBefore = issuedUtc.UtcDateTime,
            Expires = expiresUtc.UtcDateTime,
            SigningCredentials = active.SigningCredentials
        };
        string token = _handler.CreateToken(descriptor);
        int encodedBytes = Encoding.UTF8.GetByteCount(token);
        SharpAccessSecurityMetrics.EncodedAccessTokenSize.Record(encodedBytes);
        if (encodedBytes > _options.SecurityLimits.MaximumEncodedAccessTokenBytes)
        {
            throw new InvalidOperationException("The encoded access token exceeds MaximumEncodedAccessTokenBytes.");
        }

        return new AccessTokenResult(token, expiresUtc);
    }

    // Disposes only a configured key ring owned by this token-service instance.
    public void Dispose()
    {
        _ownedKeyRing?.Dispose();
        GC.SuppressFinalize(this);
    }

}
