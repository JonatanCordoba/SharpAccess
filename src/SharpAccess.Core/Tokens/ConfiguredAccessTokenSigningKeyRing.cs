using System.Security.Cryptography;
using System.Text;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess;

internal sealed class ConfiguredAccessTokenSigningKeyRing : IAccessTokenSigningKeyRing, IDisposable
{
    private const string Base64Prefix = "base64:";
    private const string Utf8Prefix = "utf8:";
    private static readonly DateTimeOffset LegacyActivation = DateTimeOffset.UnixEpoch;
    private readonly List<byte[]> _ownedKeyMaterial = [];
    private bool _disposed;

    // Builds configured signing and verification entries using the injected project clock.
    public ConfiguredAccessTokenSigningKeyRing(
        IOptions<AuthOptions> configuredOptions,
        IAuthClock clock)
    {
        ArgumentNullException.ThrowIfNull(configuredOptions);
        ArgumentNullException.ThrowIfNull(clock);
        DateTimeOffset now = clock.UtcNow;
        AuthOptions options = configuredOptions.Value;
        AccessTokenSigningOptions signing = options.AccessTokenSigning;
        if (signing.UseHostKeyRing)
        {
            throw new InvalidOperationException(
                "AccessTokenSigning.UseHostKeyRing is enabled, but no host IAccessTokenSigningKeyRing replaced the configured key ring.");
        }

        List<AccessTokenVerificationKey> verificationKeys = [];
        Dictionary<string, AccessTokenSigningKey> signingKeys = new(StringComparer.Ordinal);
        if (signing.HmacSha256Keys.Count > 0)
        {
            foreach ((string keyId, HmacAccessTokenSigningKeyOptions keyOptions) in signing.HmacSha256Keys)
            {
                byte[] material = DecodeKey(keyOptions.Key);
                _ownedKeyMaterial.Add(material);
                SymmetricSecurityKey securityKey = new(material) { KeyId = keyId };
                AccessTokenSigningKey key = new(
                    keyId,
                    new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256),
                    securityKey,
                    keyOptions.ActivatedUtc,
                    keyOptions.NotBeforeUtc,
                    keyOptions.RetiredUtc);
                AccessTokenKeyRingGuard.ValidateKey(key, requirePrivateSigningMaterial: true, now: now);
                signingKeys.Add(keyId, key);
                verificationKeys.Add(key);
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.JwtSigningKey))
        {
            byte[] material = DecodeKey(options.JwtSigningKey);
            _ownedKeyMaterial.Add(material);
            string keyId = CreateLegacyKeyId(material);
            SymmetricSecurityKey securityKey = new(material) { KeyId = keyId };
            AccessTokenSigningKey key = new(
                keyId,
                new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256),
                securityKey,
                LegacyActivation);
            AccessTokenKeyRingGuard.ValidateKey(key, requirePrivateSigningMaterial: true, now: now);
            signingKeys.Add(keyId, key);
            verificationKeys.Add(key);
        }

        if (signingKeys.Count == 0)
        {
            throw new InvalidOperationException(
                "No access-token signing key is configured. Configure AccessTokenSigning.HmacSha256Keys, register a host IAccessTokenSigningKeyRing, or provide JwtSigningKey only during migration.");
        }

        string activeKeyId = string.IsNullOrWhiteSpace(signing.ActiveKeyId)
            ? signingKeys.Count == 1 ? signingKeys.Keys.Single() : string.Empty
            : signing.ActiveKeyId;
        if (!signingKeys.TryGetValue(activeKeyId, out AccessTokenSigningKey? active))
        {
            throw new InvalidOperationException("AccessTokenSigning.ActiveKeyId must identify one configured signing key.");
        }

        ActiveSigningKey = active;
        VerificationKeys = verificationKeys.AsReadOnly();
        AccessTokenKeyRingGuard.Validate(this, now);
    }

    public AccessTokenSigningKey ActiveSigningKey { get; }
    public IReadOnlyCollection<AccessTokenVerificationKey> VerificationKeys { get; }

    // Clears decoded symmetric key material owned by this configured ring.
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (byte[] material in _ownedKeyMaterial)
        {
            CryptographicOperations.ZeroMemory(material);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // Decodes explicitly prefixed material and preserves the legacy Base64-or-UTF8 interpretation for compatibility.
    private static byte[] DecodeKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.StartsWith(Base64Prefix, StringComparison.OrdinalIgnoreCase))
        {
            string encoded = value[Base64Prefix.Length..];
            if (string.IsNullOrWhiteSpace(encoded)) { throw new FormatException("Base64 signing material cannot be empty."); }
            return Convert.FromBase64String(encoded);
        }

        if (value.StartsWith(Utf8Prefix, StringComparison.OrdinalIgnoreCase))
        {
            string text = value[Utf8Prefix.Length..];
            if (string.IsNullOrEmpty(text)) { throw new FormatException("UTF-8 signing material cannot be empty."); }
            return Encoding.UTF8.GetBytes(text);
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(value);
        }
    }

    // Derives a stable non-secret legacy key identifier from signing material.
    private static string CreateLegacyKeyId(byte[] material)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(material, digest);
        return "legacy-hs256-" + Convert.ToHexString(digest[..8]).ToLowerInvariant();
    }
}
