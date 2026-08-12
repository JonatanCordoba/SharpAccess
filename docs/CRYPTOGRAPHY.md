# SharpAccess cryptography and authentication hardening

SharpAccess uses explicit rotation contracts and bounded cryptographic work.

## Access-token signing key ring

`IAccessTokenSigningKeyRing` supplies one active signing key and accepted verification keys. Keys have stable `KeyId` values plus algorithm and activation/retirement boundaries. JWTs carry `kid`; unknown, duplicate, not-yet-valid, or retired identifiers fail closed.

Supported reviewed algorithms include HS256 with at least 256-bit material, RS256 with at least 2048-bit RSA, ES256/P-256, and valid X.509-backed RSA/ECDSA credentials. Prefer asymmetric signing where verification services should not possess signing authority.

Deploy new verification material before making it active, wait through the maximum token lifetime plus skew, then retire the prior key. Production rejects migration-only/single-key fallback behavior that does not satisfy current validation policy.

The published RC1 release included the required tested asymmetric-production recipe. Future releases must retain equivalent coverage when the current release policy requires it; do not describe that requirement as still pending before RC1 publication.

## Token-hashing and pepper rotation

Versioned token-hashing keys protect opaque refresh/reset/verification/state/exchange lookups. New values use the active version; historical accepted versions support bounded migration windows. Refresh rotation writes the active version.

Password hashes carry a pepper version. Successful login can rehash with the current pepper. Keep prior pepper versions only for the reviewed migration window; suspected compromise may require forced reset/session revocation.

## Bounded Argon2 work

Argon2 cost is not reduced under load. Maximum concurrent hashes, queued hashes, and queue wait are bounded. Secret roles must use independent material; production rejects predictable, undersized, or reused secrets.

## Secret handling

Load secrets/private signing material from an approved Windows/host secret store, certificate store, HSM, or managed-key system. Do not place them in source, images, command lines, generated migration scripts, logs, metrics, traces, or exception messages.

SQLite and PostgreSQL must maintain equivalent durable enforcement of configured token/session/security limits.
