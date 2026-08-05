using System.IdentityModel.Tokens.Jwt;
using SharpAccess;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Security;
using SharpAccess.Tokens;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class CryptographyAndTokenTests
{
    private static readonly string[] AdminRoles = [AuthRoles.Admin];
    private static readonly string[] UserReadPermissions = [AuthPermissions.UsersRead];
    private static readonly string[] TenantRoles = [TenantAuthRoles.Manager];
    private static readonly string[] TenantPermissions = [TenantAuthPermissions.MembersManage];

    // Verifies that opaque tokens are random and hashed with hmac.
    [Fact]
    public void OpaqueTokensAreRandomAndHashedWithHmac()
    {
        using HmacTokenProtector protector = new(Options.Create(TestOptions.Create()));
        string first = protector.Generate();
        string second = protector.Generate();
        Assert.NotEqual(first, second);
        Assert.NotEqual(first, protector.Hash(first));
        Assert.Equal(protector.Hash(first), protector.Hash(first));
    }

    // Verifies that argon2id hashes verify and wrong passwords fail.
    [Trait("MutationInvariant", "PasswordVerification")]
    [Fact]
    public async Task Argon2idHashesVerifyAndWrongPasswordsFail()
    {
        Argon2idPasswordHasher hasher = new(Options.Create(TestOptions.Create()));
        string encoded = await hasher.HashAsync("ValidPassword123");
        Assert.StartsWith("argon2id$v=19$", encoded, StringComparison.Ordinal);
        Assert.Equal(PasswordVerificationStatus.Success, await hasher.VerifyAsync("ValidPassword123", encoded));
        Assert.Equal(PasswordVerificationStatus.Failed, await hasher.VerifyAsync("WrongPassword123", encoded));
    }

    // Verifies that pepper rotation requests rehash without rejecting old hash.
    [Fact]
    public async Task PepperRotationRequestsRehashWithoutRejectingOldHash()
    {
        AuthOptions oldOptions = TestOptions.Create();
        Argon2idPasswordHasher oldHasher = new(Options.Create(oldOptions));
        string encoded = await oldHasher.HashAsync("ValidPassword123");

        AuthOptions newOptions = TestOptions.Create();
        newOptions.Passwords.CurrentPepperVersion = "v2";
        newOptions.Passwords.Peppers["v2"] = "TEST-PASSWORD-PEPPER-V2-12345678901234567";
        Argon2idPasswordHasher newHasher = new(Options.Create(newOptions));
        Assert.Equal(
            PasswordVerificationStatus.SuccessNeedsRehash,
            await newHasher.VerifyAsync("ValidPassword123", encoded));
    }

    // Verifies that JWT authorization claims preserve global, tenant, owner, and version boundaries.
    [Fact]
    public void JwtContainsExplicitlyScopedAuthorizationClaims()
    {
        AuthOptions options = TestOptions.Create();
        JwtAccessTokenService service = new(Options.Create(options), new FixedClock());
        Guid tenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        EffectiveAuthorizationContext authorization = new(
            new GlobalAuthorizationContext(AdminRoles, UserReadPermissions),
            new TenantAuthorizationContext(
                tenantId,
                IsOwner: true,
                TenantRoles,
                TenantPermissions),
            AuthorizationVersion: 7);
        UserContext user = new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "person@example.com",
            true,
            authorization,
            SecurityVersion: 7);

        AccessTokenResult result = service.Create(user);
        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal("test-issuer", token.Issuer);
        Assert.Contains(token.Claims, claim => claim.Type == JwtRegisteredClaimNames.Jti);
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.SecurityVersionClaim && claim.Value == "7");
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.AuthorizationVersionClaim && claim.Value == "7");
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.GlobalRoleClaim && claim.Value == AuthRoles.Admin);
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.GlobalPermissionClaim && claim.Value == AuthPermissions.UsersRead);
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.TenantRoleClaim && claim.Value == TenantAuthRoles.Manager);
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.TenantPermissionClaim && claim.Value == TenantAuthPermissions.MembersManage);
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.TenantClaim && claim.Value == tenantId.ToString("D"));
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.TenantOwnerClaim && claim.Value == tenantId.ToString("D"));
        Assert.DoesNotContain(token.Claims, claim => claim.Type == "permission");
        Assert.DoesNotContain(token.Claims, claim => claim.Type == "role");
        Assert.DoesNotContain(token.Claims, claim =>
            claim.Type == AuthConstants.GlobalPermissionClaim
            && claim.Value == TenantAuthPermissions.MembersManage);
    }

    // Verifies that a non-owner tenant context never receives an owner claim.
    [Fact]
    public void JwtOmitsOwnerClaimForNonOwnerTenantContext()
    {
        AuthOptions options = TestOptions.Create();
        JwtAccessTokenService service = new(Options.Create(options), new FixedClock());
        Guid tenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        UserContext user = new(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "member@example.com",
            true,
            new EffectiveAuthorizationContext(
                new GlobalAuthorizationContext([], []),
                new TenantAuthorizationContext(tenantId, false, [TenantAuthRoles.Member], [TenantAuthPermissions.TenantRead]),
                AuthorizationVersion: 2),
            SecurityVersion: 2);

        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(service.Create(user).Token);

        Assert.DoesNotContain(token.Claims, claim => claim.Type == AuthConstants.TenantOwnerClaim);
        Assert.Contains(token.Claims, claim => claim.Type == AuthConstants.TenantPermissionClaim && claim.Value == TenantAuthPermissions.TenantRead);
        Assert.DoesNotContain(token.Claims, claim => claim.Type == AuthConstants.GlobalPermissionClaim && claim.Value == TenantAuthPermissions.TenantRead);
    }

    // Verifies that hashing honors pre cancelled tokens.
    [Fact]
    public async Task HashingHonorsPreCancelledTokens()
    {
        Argon2idPasswordHasher hasher = new(Options.Create(TestOptions.Create()));
        using CancellationTokenSource source = new();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hasher.HashAsync("ValidPassword123", source.Token));
    }

    private sealed class FixedClock : IAuthClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    }
}
