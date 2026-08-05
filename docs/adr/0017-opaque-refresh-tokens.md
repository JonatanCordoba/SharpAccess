# ADR 0017: Use opaque refresh tokens with rotation and family revocation

## Status

Accepted.

## Context

Refresh tokens grant long-lived access to the authentication system. They are higher-value credentials than access tokens and must remain safe if the database is read, application logs are compromised, or a browser token is replayed after legitimate use.

Self-contained refresh tokens would make revocation and replay detection harder. Persisted opaque tokens allow strict server-side control, but the persisted value must not be usable as a bearer secret if stolen.

## Decision

Refresh tokens are opaque random values shown to the client once. The provider persists only a keyed hash of each token.

Refresh tokens must:

- Use cryptographically secure randomness.
- Be hashed before persistence with the configured token hashing key.
- Rotate on every successful refresh.
- Preserve a family identifier across rotations.
- Revoke the family when reuse is detected.
- Store enough metadata for audit and targeted revocation without storing the raw token.

## Consequences

Positive:

- Database reads do not directly expose bearer refresh tokens.
- Reuse detection identifies likely theft or replay.
- Family revocation limits attacker persistence.
- Logout and explicit revocation are deterministic server-side operations.

Trade-offs:

- Refresh requires a database lookup.
- Rotation must be atomic at the provider boundary.
- Token hashing-key rotation invalidates outstanding refresh tokens unless multi-key support is added later.

## Guardrails

- Never log raw refresh tokens.
- Never expose refresh-token hashes to clients.
- Keep refresh-token rotation in one transactional provider operation.
- Provider-contract tests must verify success, reuse, expiration, revoked tokens, invalid users, and family revocation.
