using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess;

internal static class AccessTokenKeyRingGuard
{
    private static readonly HashSet<string> AllowedAlgorithms = new(StringComparer.Ordinal)
    {
        SecurityAlgorithms.HmacSha256,
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.EcdsaSha256
    };

    // Validates key-ring composition and requires an active key accepted at the supplied time.
    internal static void Validate(IAccessTokenSigningKeyRing ring, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(ring.ActiveSigningKey);
        ArgumentNullException.ThrowIfNull(ring.VerificationKeys);
        if (ring.VerificationKeys.Count == 0)
        {
            throw new InvalidOperationException("The access-token key ring must contain verification keys.");
        }

        HashSet<string> identifiers = new(StringComparer.Ordinal);
        foreach (AccessTokenVerificationKey key in ring.VerificationKeys)
        {
            ValidateKey(key, requirePrivateSigningMaterial: false, now: now);
            if (!identifiers.Add(key.KeyId))
            {
                throw new InvalidOperationException($"The access-token key ring contains duplicate key identifier '{key.KeyId}'.");
            }
        }

        AccessTokenSigningKey active = ring.ActiveSigningKey;
        ValidateKey(active, requirePrivateSigningMaterial: true, now: now);
        AccessTokenVerificationKey? accepted = ring.VerificationKeys.SingleOrDefault(
            key => string.Equals(key.KeyId, active.KeyId, StringComparison.Ordinal));
        if (accepted is null
            || !string.Equals(accepted.Algorithm, active.Algorithm, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The active signing key must also appear in the accepted verification-key collection.");
        }

        if (!IsAccepted(active, now))
        {
            throw new InvalidOperationException("The active signing key is not valid at the current time.");
        }
    }

    // Validates one key's algorithm, material, and optional private-signing requirement.
    internal static void ValidateKey(
        AccessTokenVerificationKey key,
        bool requirePrivateSigningMaterial,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!AllowedAlgorithms.Contains(key.Algorithm))
        {
            throw new InvalidOperationException($"JWT algorithm '{key.Algorithm}' is not supported.");
        }

        ValidateSecurityKey(
            key.VerificationKey,
            key.Algorithm,
            requirePrivateSigningMaterial: false,
            now: now);
        if (key is AccessTokenSigningKey signing)
        {
            if (!string.Equals(signing.SigningCredentials.Algorithm, key.Algorithm, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Signing credentials and verification metadata must use the same algorithm.");
            }

            ValidateSecurityKey(
                signing.SigningCredentials.Key,
                key.Algorithm,
                requirePrivateSigningMaterial,
                now);
        }
    }

    // Enforces algorithm-specific key type, size, and certificate validity requirements.
    private static void ValidateSecurityKey(
        SecurityKey securityKey,
        string algorithm,
        bool requirePrivateSigningMaterial,
        DateTimeOffset now)
    {
        if (securityKey is SymmetricSecurityKey symmetric)
        {
            ValidateSymmetricKey(symmetric, algorithm);
            return;
        }

        if (securityKey is RsaSecurityKey rsa)
        {
            ValidateRsaKey(rsa, algorithm);
            return;
        }

        if (securityKey is ECDsaSecurityKey ecdsa)
        {
            ValidateEcdsaKey(ecdsa, algorithm);
            return;
        }

        if (securityKey is X509SecurityKey certificateKey)
        {
            ValidateCertificateKey(certificateKey, algorithm, requirePrivateSigningMaterial, now);
            return;
        }

        throw KeyTypeMismatch();
    }

    private static void ValidateSymmetricKey(SymmetricSecurityKey key, string algorithm)
    {
        if (!string.Equals(algorithm, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
        {
            throw KeyTypeMismatch();
        }

        if (key.KeySize < 256)
        {
            throw new InvalidOperationException("HMAC-SHA-256 keys must contain at least 256 bits.");
        }
    }

    private static void ValidateRsaKey(RsaSecurityKey key, string algorithm)
    {
        if (!string.Equals(algorithm, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
        {
            throw KeyTypeMismatch();
        }

        if (key.KeySize < 2048)
        {
            throw new InvalidOperationException("RSA signing keys must contain at least 2048 bits.");
        }
    }

    private static void ValidateEcdsaKey(ECDsaSecurityKey key, string algorithm)
    {
        if (!string.Equals(algorithm, SecurityAlgorithms.EcdsaSha256, StringComparison.Ordinal))
        {
            throw KeyTypeMismatch();
        }

        if (key.KeySize != 256)
        {
            throw new InvalidOperationException("ES256 requires an ECDSA P-256 key.");
        }
    }

    private static void ValidateCertificateKey(
        X509SecurityKey key,
        string algorithm,
        bool requirePrivateSigningMaterial,
        DateTimeOffset now)
    {
        if (algorithm is not SecurityAlgorithms.RsaSha256 and not SecurityAlgorithms.EcdsaSha256)
        {
            throw KeyTypeMismatch();
        }

        ValidateCertificate(key.Certificate, requirePrivateSigningMaterial, algorithm, now);
    }

    private static InvalidOperationException KeyTypeMismatch() =>
        new("The security-key type does not match the configured JWT algorithm.");

    // Reports whether a key is inside its activation, not-before, and retirement window.
    internal static bool IsAccepted(AccessTokenVerificationKey key, DateTimeOffset now) =>
        key.ActivatedUtc <= now
        && (!key.NotBeforeUtc.HasValue || key.NotBeforeUtc.Value <= now)
        && (!key.RetiredUtc.HasValue || now < key.RetiredUtc.Value);

    // Validates certificate time, private material, algorithm, and curve or modulus shape.
    private static void ValidateCertificate(
        X509Certificate2 certificate,
        bool requirePrivateSigningMaterial,
        string algorithm,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (certificate.NotBefore.ToUniversalTime() > now.UtcDateTime
            || certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime)
        {
            throw new InvalidOperationException("The signing certificate is not currently valid.");
        }

        if (requirePrivateSigningMaterial && !certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The active signing certificate must contain private signing material.");
        }

        using RSA? rsa = certificate.GetRSAPublicKey();
        using ECDsa? ecdsa = certificate.GetECDsaPublicKey();
        if (algorithm == SecurityAlgorithms.RsaSha256 && rsa is null)
        {
            throw new InvalidOperationException("RS256 requires RSA public-key material in the signing certificate.");
        }

        if (algorithm == SecurityAlgorithms.EcdsaSha256
            && (ecdsa is null || ecdsa.KeySize != 256))
        {
            throw new InvalidOperationException("ES256 requires ECDSA P-256 public-key material in the signing certificate.");
        }
    }
}
