using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests;

public sealed class AsymmetricSigningRecipeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void X509BackedRsaKeyRingIsAccepted()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=SharpAccess signing recipe",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            Now.AddDays(-1),
            Now.AddDays(30));

        X509SecurityKey key = new(certificate) { KeyId = "2026-07" };
        AccessTokenSigningKey active = new(
            "2026-07",
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            key,
            Now.AddDays(-1));
        IAccessTokenSigningKeyRing ring = new TestKeyRing(active, [active]);

        AccessTokenKeyRingGuard.Validate(ring, Now);

        Assert.True(certificate.HasPrivateKey);
        Assert.Equal(SecurityAlgorithms.RsaSha256, active.Algorithm);
    }

    private sealed class TestKeyRing(
        AccessTokenSigningKey activeSigningKey,
        IReadOnlyCollection<AccessTokenVerificationKey> verificationKeys)
        : IAccessTokenSigningKeyRing
    {
        public AccessTokenSigningKey ActiveSigningKey { get; } = activeSigningKey;
        public IReadOnlyCollection<AccessTokenVerificationKey> VerificationKeys { get; } = verificationKeys;
    }
}
