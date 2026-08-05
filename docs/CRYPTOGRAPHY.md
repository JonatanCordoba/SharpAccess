# SharpAccess cryptography and authentication hardening

SharpAccess replaces implicit single-key behavior with explicit rotation contracts and bounded cryptographic work.

## Access-token signing key ring

`IAccessTokenSigningKeyRing` supplies one active signing key and all verification keys accepted during a rotation window. Every key has a stable `KeyId`, an explicit algorithm, activation time, optional not-before time, and optional retirement time. SharpAccess writes `kid` into every JWT and validation resolves exactly one currently accepted key by that identifier. Missing, unknown, duplicate, not-yet-valid, or retired identifiers fail closed.

The stable algorithm allowlist is:

- HMAC-SHA-256 with at least 256 bits;
- RSA-SHA-256 with at least 2048 bits;
- ECDSA P-256/SHA-256;
- X.509-backed RSA or ECDSA credentials that are currently valid and contain the required signing material.

### Deployment guidance

Use the built-in configured HS256 ring for a single tightly controlled trust boundary. Prefer a host-owned asymmetric ring for multi-service validation, separation of signing and verification authority, certificate-based rotation, HSM-backed operation, or a managed-key system. Verification-only services should receive public key material rather than signing authority.

### Built-in HMAC rotation

```csharp
builder.Services.AddSharpAccess(builder.Configuration, options =>
{
    options.AccessTokenSigning.ActiveKeyId = "2026-07";
    options.AccessTokenSigning.HmacSha256Keys["2026-07"] = new()
    {
        Key = builder.Configuration["SharpAccess:SigningKeys:2026-07"]!,
        ActivatedUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
    };
    options.AccessTokenSigning.HmacSha256Keys["2026-04"] = new()
    {
        Key = builder.Configuration["SharpAccess:SigningKeys:2026-04"]!,
        ActivatedUtc = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
        RetiredUtc = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero)
    };
});
```

Deploy the new verification key to every instance, then make it active, wait at least the maximum access-token lifetime plus clock skew, and only then retire the old key. Removing or retiring a key immediately invalidates tokens carrying its `kid`.

`JwtSigningKey` remains a migration bridge for existing configurations. It receives a stable non-secret-derived identifier, but Production validation rejects this single-key path. No random signing fallback exists.

### Host-owned RSA, ECDSA, HSM, or certificate credentials

Register an `IAccessTokenSigningKeyRing` before `AddSharpAccess`, or replace the default registration afterward. Set `AccessTokenSigning.UseHostKeyRing = true`. The active entry carries host-provided `SigningCredentials`; verification entries may contain public-only `SecurityKey` material. SharpAccess never logs key material and does not dispose host-owned keys.

Before public release-candidate publication, retain at least one tested production recipe using RS256, ES256, or X.509-backed signing. Vendor-specific managed-key integrations belong in optional packages or host code rather than `SharpAccess.Core`.

## Token-hashing rotation

`TokenHashing.CurrentKeyVersion` identifies the key used for new refresh, verification, reset, and state tokens. `TokenHashing.Keys` contains the active key and accepted historical keys. Persisted hashes use a fixed-width version-tagged HMAC envelope, so the accepted key version is encoded without changing the provider column width or exposing the raw token. Lookup computes bounded candidates only for configured accepted versions.

Refresh rotation always writes the active version. Remove a historical key only after its token lifetime has elapsed or after intentionally revoking affected sessions. The legacy `TokenHashing.Key` path accepts the previous unversioned hash format during migration. When using `Keys`, keep `LegacyUnversionedKeyVersion` pointed at the version that produced earlier rows; set it to `null` after that compatibility window closes.

```csharp
options.TokenHashing.CurrentKeyVersion = "v2";
options.TokenHashing.Keys["v2"] = configuration["SharpAccess:TokenHash:v2"]!;
options.TokenHashing.Keys["v1"] = configuration["SharpAccess:TokenHash:v1"]!;
```

## Password peppers and Argon2id

Password hashes carry a pepper version. Keep the previous pepper while users sign in; successful verification returns a rehash signal and login-time rehash upgrades the stored value. The `sharpaccess.password_hash.rehash_required` counter records this path. Duplicate material across peppers, signing keys, token HMAC keys, OAuth secrets, and the rate-limit partition key is rejected by Production validation.

Argon2 cost parameters are never reduced under load. `MaximumConcurrentPasswordHashes`, `MaximumQueuedPasswordHashes`, and `PasswordHashQueueTimeout` bound process-wide work and queued requests. `MaximumQueuedPasswordHashes = 0` is a supported no-wait mode: work starts immediately only when a hash slot is already available; otherwise the request fails with the normal queue-full result without entering a queue. Queue duration, hash duration, and active-hash instruments are emitted by the `SharpAccess.Security` meter.

Configuration validation, signing-key activation and retirement checks, and JWT key resolution use the registered `IAuthClock`. Tests and hosts can therefore make time-bound key behavior deterministic without changing the process clock.

Recommended rotation order:

1. Add the new pepper while retaining the prior version.
2. Set `CurrentPepperVersion` to the new identifier.
3. Observe the rehash-required counter and login success/error rates.
4. Keep the old pepper through the expected account return window.
5. Removing the old pepper intentionally makes remaining hashes using it unverifiable; use an account recovery campaign before forced retirement.

## Production secret validation

Production rejects migration-only signing configuration, sample/default/predictable strings, repeated-byte values, undersized keys, and material reused across secret roles. Custom key rings are validated when JWT services are resolved: unsupported algorithms, weak RSA/HMAC sizes, non-P-256 ECDSA, invalid certificates, and missing active private material fail startup.

Load secrets and private signing material from an approved secret store, certificate store, HSM, or managed-key integration. Do not place them in source, container images, command lines, generated migration scripts, logs, metrics, traces, or exception messages.

## Token size and refresh-family limits

`AuthOptions.SecurityLimits` bounds roles, permissions, final encoded access-token bytes, active refresh families per user, and active tokens per family. Exceeding a limit fails deterministically before a session is issued or inside the owning provider transaction. SQLite and PostgreSQL must continue to provide equivalent durable enforcement evidence and must never silently increase configured limits.
