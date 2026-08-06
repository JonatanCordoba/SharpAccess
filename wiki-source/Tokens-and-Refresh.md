# Tokens and Refresh

SharpAccess uses signed JWT access tokens and rotating opaque refresh tokens.

## Access tokens

Access-token validation checks:

- approved algorithm;
- `kid` resolution;
- issuer;
- audience;
- lifetime and clock skew;
- bounded claims;
- persisted account state and security version;
- persisted authorization version;
- active tenant membership for tenant-scoped tokens.

Unknown or retired key identifiers fail closed.

## Refresh tokens

Refresh tokens are opaque bearer values. The database stores only version-tagged keyed hashes.

On successful refresh, SharpAccess:

1. validates the token and owning account;
2. rotates the token;
3. persists the replacement;
4. revokes or updates the previous token as required;
5. emits the canonical audit outcome inside the provider transaction.

Replay detection revokes the refresh family. Refresh-family and per-family token counts are bounded.

## Key rotation

- New access tokens use the active signing key; retained verification keys can validate bounded-lifetime existing tokens.
- New opaque tokens use the active token-hashing key version.
- Retained token-hashing keys allow existing bounded-lifetime values to remain discoverable during controlled rotation.
- Emergency key removal can intentionally invalidate affected values.

## Browser protections

For browser clients:

- keep refresh tokens in secure HttpOnly cookies;
- require the configured request-confirmation header for cookie-backed refresh and logout;
- do not persist access tokens in local or session storage;
- use a restrictive content-security policy.

## Capacity limits

The default security contract bounds active refresh families per user and active refresh tokens per family. See [Performance and Capacity](Performance-and-Capacity).

## References

- [Cryptography](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/CRYPTOGRAPHY.md)
- [Security design](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/SECURITY.md)
- [Opaque refresh-token ADR](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/adr/0017-opaque-refresh-tokens.md)
- [Threat model](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/THREAT_MODEL.md)
