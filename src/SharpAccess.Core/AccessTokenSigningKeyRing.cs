using Microsoft.IdentityModel.Tokens;

namespace SharpAccess;

/// <summary>Describes one accepted JWT verification key and its validity window.</summary>
public class AccessTokenVerificationKey
{
    /// <summary>Creates verification-key metadata with a validated identifier and validity window.</summary>
    /// <param name="keyId">The stable JWT key identifier.</param>
    /// <param name="verificationKey">The key material used to verify signatures.</param>
    /// <param name="algorithm">The exact JWT signing algorithm accepted for this key.</param>
    /// <param name="activatedUtc">The activation timestamp used for deterministic key ordering.</param>
    /// <param name="notBeforeUtc">The optional earliest instant at which the key may verify tokens.</param>
    /// <param name="retiredUtc">The optional instant after which the key is no longer accepted.</param>
    /// <exception cref="System.ArgumentException">The identifier, algorithm, or validity window is invalid, or the supplied key carries a different identifier.</exception>
    /// <exception cref="System.ArgumentNullException">The verification key is null.</exception>
    public AccessTokenVerificationKey(
        string keyId,
        SecurityKey verificationKey,
        string algorithm,
        DateTimeOffset activatedUtc,
        DateTimeOffset? notBeforeUtc = null,
        DateTimeOffset? retiredUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(verificationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        if (keyId.Length > 128 || keyId.Any(char.IsControl))
        {
            throw new ArgumentException("Key identifiers must be no longer than 128 characters and contain no controls.", nameof(keyId));
        }

        if (notBeforeUtc.HasValue && retiredUtc.HasValue && notBeforeUtc.Value >= retiredUtc.Value)
        {
            throw new ArgumentException("The key not-before date must precede its retirement date.", nameof(notBeforeUtc));
        }

        if (!string.IsNullOrWhiteSpace(verificationKey.KeyId)
            && !string.Equals(verificationKey.KeyId, keyId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The verification key identifier must match keyId.", nameof(verificationKey));
        }

        verificationKey.KeyId = keyId;
        KeyId = keyId;
        VerificationKey = verificationKey;
        Algorithm = algorithm;
        ActivatedUtc = activatedUtc;
        NotBeforeUtc = notBeforeUtc;
        RetiredUtc = retiredUtc;
    }

    /// <summary>Gets the stable JWT key identifier.</summary>
    public string KeyId { get; }
    /// <summary>Gets the key material used to verify signatures.</summary>
    public SecurityKey VerificationKey { get; }
    /// <summary>Gets the exact JWT signing algorithm accepted for this key.</summary>
    public string Algorithm { get; }
    /// <summary>Gets the activation timestamp used for deterministic key ordering.</summary>
    public DateTimeOffset ActivatedUtc { get; }
    /// <summary>Gets the optional earliest UTC instant at which the key may verify tokens.</summary>
    public DateTimeOffset? NotBeforeUtc { get; }
    /// <summary>Gets the optional UTC instant after which the key is no longer accepted.</summary>
    public DateTimeOffset? RetiredUtc { get; }
}

/// <summary>Describes the active JWT signing key and the public or symmetric verification material paired with it.</summary>
public sealed class AccessTokenSigningKey : AccessTokenVerificationKey
{
    /// <summary>Creates an active signing entry paired with matching verification material.</summary>
    /// <param name="keyId">The stable JWT key identifier shared by the signing and verification keys.</param>
    /// <param name="signingCredentials">The credentials used to sign new access tokens.</param>
    /// <param name="verificationKey">The key material distributed for signature verification.</param>
    /// <param name="activatedUtc">The activation timestamp used for deterministic key ordering.</param>
    /// <param name="notBeforeUtc">The optional earliest instant at which the key may verify tokens.</param>
    /// <param name="retiredUtc">The optional instant after which the key is no longer accepted.</param>
    /// <exception cref="System.ArgumentException">The signing or verification key identifier does not match keyId, or the validity window is invalid.</exception>
    /// <exception cref="System.ArgumentNullException">The signing credentials or verification key is null.</exception>
    public AccessTokenSigningKey(
        string keyId,
        SigningCredentials signingCredentials,
        SecurityKey verificationKey,
        DateTimeOffset activatedUtc,
        DateTimeOffset? notBeforeUtc = null,
        DateTimeOffset? retiredUtc = null)
        : base(
            keyId,
            verificationKey,
            signingCredentials?.Algorithm ?? throw new ArgumentNullException(nameof(signingCredentials)),
            activatedUtc,
            notBeforeUtc,
            retiredUtc)
    {
        if (!string.IsNullOrWhiteSpace(signingCredentials.Key.KeyId)
            && !string.Equals(signingCredentials.Key.KeyId, keyId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The signing-credential key identifier must match keyId.", nameof(signingCredentials));
        }

        signingCredentials.Key.KeyId = keyId;
        SigningCredentials = signingCredentials;
    }

    /// <summary>Gets the credentials used to sign new access tokens.</summary>
    public SigningCredentials SigningCredentials { get; }
}

/// <summary>Supplies the active signing key and every key accepted during a controlled JWT rotation window.</summary>
public interface IAccessTokenSigningKeyRing
{
    /// <summary>Gets the key used to sign newly issued access tokens.</summary>
    AccessTokenSigningKey ActiveSigningKey { get; }
    /// <summary>Gets every key currently accepted for access-token signature verification, including the active key.</summary>
    IReadOnlyCollection<AccessTokenVerificationKey> VerificationKeys { get; }
}
