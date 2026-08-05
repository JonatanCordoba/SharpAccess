using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace SharpAccess.UnitTests.Security;

public sealed class AccessTokenKeyRingGuardCertificateTests
{
    [Fact]
    public void ValidateKeyAcceptsValidRsaSigningCertificate()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = CreateRsaCertificate(now.AddDays(-1), now.AddDays(1));
        AccessTokenSigningKey key = CreateSigningKey(certificate, SecurityAlgorithms.RsaSha256, now);

        AccessTokenKeyRingGuard.ValidateKey(key, requirePrivateSigningMaterial: true, now);
    }

    [Theory]
    [InlineData(-3, -2)]
    [InlineData(2, 3)]
    public void ValidateKeyRejectsCertificateOutsideValidityWindow(int notBeforeDays, int notAfterDays)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = CreateRsaCertificate(
            now.AddDays(notBeforeDays),
            now.AddDays(notAfterDays));
        AccessTokenVerificationKey key = CreateVerificationKey(certificate, SecurityAlgorithms.RsaSha256, now);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AccessTokenKeyRingGuard.ValidateKey(key, requirePrivateSigningMaterial: false, now));

        Assert.Equal("The signing certificate is not currently valid.", exception.Message);
    }

    [Fact]
    public void ValidateKeyRejectsPublicOnlyActiveSigningCertificate()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 privateCertificate = CreateRsaCertificate(now.AddDays(-1), now.AddDays(1));
        using X509Certificate2 publicCertificate = X509CertificateLoader.LoadCertificate(
            privateCertificate.Export(X509ContentType.Cert));
        AccessTokenSigningKey key = CreateSigningKey(publicCertificate, SecurityAlgorithms.RsaSha256, now);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AccessTokenKeyRingGuard.ValidateKey(key, requirePrivateSigningMaterial: true, now));

        Assert.Equal("The active signing certificate must contain private signing material.", exception.Message);
    }

    [Fact]
    public void ValidateKeyRejectsRsaCertificateConfiguredForEs256()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = CreateRsaCertificate(now.AddDays(-1), now.AddDays(1));
        AccessTokenVerificationKey key = CreateVerificationKey(certificate, SecurityAlgorithms.EcdsaSha256, now);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AccessTokenKeyRingGuard.ValidateKey(key, requirePrivateSigningMaterial: false, now));

        Assert.Equal("ES256 requires ECDSA P-256 public-key material in the signing certificate.", exception.Message);
    }

    [Fact]
    public void ValidateKeyAcceptsP256CertificateForEs256()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = CreateEcdsaCertificate(ECCurve.NamedCurves.nistP256, now.AddDays(-1), now.AddDays(1));
        AccessTokenSigningKey key = CreateSigningKey(certificate, SecurityAlgorithms.EcdsaSha256, now);

        AccessTokenKeyRingGuard.ValidateKey(key, requirePrivateSigningMaterial: true, now);
    }

    [Fact]
    public void ValidateKeyRejectsNonP256CertificateForEs256()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = CreateEcdsaCertificate(ECCurve.NamedCurves.nistP384, now.AddDays(-1), now.AddDays(1));
        AccessTokenVerificationKey key = CreateVerificationKey(certificate, SecurityAlgorithms.EcdsaSha256, now);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AccessTokenKeyRingGuard.ValidateKey(key, requirePrivateSigningMaterial: false, now));

        Assert.Equal("ES256 requires ECDSA P-256 public-key material in the signing certificate.", exception.Message);
    }

    private static AccessTokenVerificationKey CreateVerificationKey(
        X509Certificate2 certificate,
        string algorithm,
        DateTimeOffset now)
    {
        const string KeyId = "certificate-key";
        return new AccessTokenVerificationKey(
            KeyId,
            new X509SecurityKey(certificate) { KeyId = KeyId },
            algorithm,
            now.AddDays(-1));
    }

    private static AccessTokenSigningKey CreateSigningKey(
        X509Certificate2 certificate,
        string algorithm,
        DateTimeOffset now)
    {
        const string KeyId = "certificate-key";
        X509SecurityKey signingKey = new(certificate) { KeyId = KeyId };
        X509SecurityKey verificationKey = new(certificate) { KeyId = KeyId };
        return new AccessTokenSigningKey(
            KeyId,
            new SigningCredentials(signingKey, algorithm),
            verificationKey,
            now.AddDays(-1));
    }

    private static X509Certificate2 CreateRsaCertificate(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=SharpAccess RSA test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static X509Certificate2 CreateEcdsaCertificate(
        ECCurve curve,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using ECDsa ecdsa = ECDsa.Create(curve);
        CertificateRequest request = new(
            "CN=SharpAccess ECDSA test",
            ecdsa,
            HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(notBefore, notAfter);
    }
}
