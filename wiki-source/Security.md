# Security

SharpAccess is designed around explicit boundaries, bounded resources, fail-closed validation, host-owned secrets, and exact provider transaction behavior.

## Core properties

- Argon2id password hashing with random salts and versioned peppers.
- Equivalent dummy work for unknown accounts.
- Bounded password-hash concurrency and queue wait.
- Rotatable access-token signing keys with strict `kid` handling.
- Opaque refresh tokens with only keyed hashes persisted.
- Refresh rotation, replay detection, and family revocation.
- Separate global and tenant authorization catalogs.
- Persisted account, security, authorization, and membership rechecks.
- Authorization Code + PKCE OIDC with exact issuer, nonce, state, and endpoint-host validation.
- Atomic security mutations and canonical transaction-local audit evidence.
- Sanitized provider-neutral failures.

## Host-owned controls

The host must protect:

- signing keys;
- token-hashing keys;
- password peppers;
- rate-limit partition key;
- OIDC credentials;
- Data Protection keys and certificates;
- DB credentials;
- email credentials.

Never log passwords, raw access or refresh tokens, reset/verification tokens, OIDC codes, state, nonces, client secrets, connection strings, or key material.

## Browser security

Use HTTPS, secure HttpOnly refresh cookies, request-confirmation headers for refresh/logout, restrictive CSP, trusted proxy configuration, and memory-only access-token handling.

## Supply chain

Release evidence includes locked restore, vulnerability audit, dependency review, SAST, secret scanning, action pinning, SBOMs, checksums, and provenance. Release artifacts must come from the exact verified revision.

## Reporting vulnerabilities

Use the repository security policy and private vulnerability reporting when enabled. Do not open a public issue containing exploit details or secrets.

## References

- [Security design](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/SECURITY.md)
- [Threat model](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/THREAT_MODEL.md)
- [Cryptography](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/CRYPTOGRAPHY.md)
- [Security policy](https://github.com/JonatanCordoba/SharpAccess/blob/main/SECURITY.md)
- [Supply chain](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/SUPPLY-CHAIN.md)
