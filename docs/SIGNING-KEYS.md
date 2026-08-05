# Access-token signing keys

SharpAccess requires a rotatable access-token signing-key ring. Production hosts should prefer a host-owned `IAccessTokenSigningKeyRing` backed by their approved certificate, HSM, secret, or managed-key system.

## Configured HMAC material

Configured HMAC values support explicit encodings:

- `base64:<value>` decodes Base64 into key bytes;
- `utf8:<value>` encodes the remaining text as UTF-8 bytes.

Existing unprefixed configuration remains backward compatible: SharpAccess first attempts Base64 and otherwise treats the value as UTF-8. New configuration should use an explicit prefix so operators and reviewers can determine the intended byte representation without inference.

Every HS256 key must contain at least 256 bits after decoding. Key identifiers are bounded, case-sensitive, and must match the active-key identifier exactly.

Use HS256 when signing and validation remain inside one tightly controlled trust boundary. Prefer asymmetric signing when several services validate tokens or verification services must not possess signing authority.

## Rotation

A key ring contains:

- one active signing key;
- every verification key accepted during the controlled overlap window;
- activation, optional not-before, and optional retirement timestamps.

The active key must be present in the verification collection, use the same algorithm and identifier, and be valid at the current time. Retired keys may remain temporarily for verification but cannot be active signing keys.

Deploy new verification material before activating the new signer. Retain the previous verification key for at least the maximum token lifetime plus allowed clock skew, then retire it deliberately.

## Asymmetric keys and certificates

Host-owned rings may use RS256 or ES256. SharpAccess validates:

- RSA keys are at least 2,048 bits;
- ES256 keys use the P-256 curve;
- signing certificates are currently valid;
- the active signing certificate contains private material;
- the configured algorithm matches the key or certificate type.

### X.509-backed RS256 recipe

Load the certificate through the host's approved certificate or key-management mechanism. Do not embed a certificate password, private key, or raw certificate bytes in source.

```csharp
using Microsoft.IdentityModel.Tokens;
using SharpAccess;
using System.Security.Cryptography.X509Certificates;

X509Certificate2 certificate = LoadSigningCertificateFromApprovedStore();
X509SecurityKey key = new(certificate) { KeyId = "2026-07" };
AccessTokenSigningKey active = new(
    "2026-07",
    new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
    key,
    activatedUtc: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

builder.Services.AddSingleton<IAccessTokenSigningKeyRing>(
    new HostSigningKeyRing(active, [active]));

builder.Services.AddSharpAccess(builder.Configuration, options =>
{
    options.AccessTokenSigning.UseHostKeyRing = true;
});

sealed class HostSigningKeyRing(
    AccessTokenSigningKey activeSigningKey,
    IReadOnlyCollection<AccessTokenVerificationKey> verificationKeys)
    : IAccessTokenSigningKeyRing
{
    public AccessTokenSigningKey ActiveSigningKey { get; } = activeSigningKey;
    public IReadOnlyCollection<AccessTokenVerificationKey> VerificationKeys { get; } = verificationKeys;
}
```

The equivalent X.509 construction and key-ring validation path is covered by `AsymmetricSigningRecipeTests`.

For key rollover, include the previous public certificate as an `AccessTokenVerificationKey` while the new certificate is active. Verification-only services should receive public certificates or public keys rather than private signing material.

## Secret handling

Do not commit signing material to source control, examples, logs, workflow output, evidence artifacts, or issue bodies. Keep rotation procedures, emergency revocation, backup, expiry monitoring, and access control under the consuming host's operational policy. SharpAccess does not dispose host-owned keys or certificates; the host owns their lifetime.
