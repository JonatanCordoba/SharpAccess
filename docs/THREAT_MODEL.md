# Threat model

## Protected assets

Password verifiers/peppers; JWT, opaque-token, rate-limit, OIDC, and Data Protection keys; verification/reset/OAuth/session tokens; user/global/tenant authorization and ownership state; memberships, OAuth links, and audit history.

## Trust boundaries

Browser/API to ASP.NET Core; trusted proxy to Kestrel; host to database/email/OIDC endpoints; deployment/secret manager to process; and global authorization versus each tenant authorization domain. Input crossing a boundary is untrusted and must be validated/bounded.

## Major mitigations

- Credential guessing: per-IP limits, atomic lockout, Argon2id, generic failures.
- Database disclosure: salted Argon2id plus external peppers, keyed token hashes, no provider-token persistence.
- Refresh theft/replay: secure cookie defaults, transactional rotation, family revocation, persisted account/version checks.
- OIDC interception/CSRF: authorization code + PKCE S256, single-use state, nonce, exact redirect URI, strict issuer/audience/signature/lifetime/host allowlists.
- Account-link takeover: verified provider email plus active/verified local-account rules.
- Cross-tenant/global escalation: separate catalogs/claims/policies, tenant-keyed joins, explicit global-or-tenant policy only where reviewed.
- Ownership takeover/orphaning: unique owner record, immutable Owner role, existing-member requirement, locked atomic transfer, session/context invalidation, canonical audit evidence.
- Injection/error disclosure: parameterized SQL, bounded identifiers, sanitized public errors.
- Concurrent state races: provider-owned transactions, serialized migrations, atomic lockout/refresh/ownership changes.

## Authorization invariants

Global roles/permissions remain global; tenant roles/permissions remain tenant-scoped; tenant claims never satisfy global policies; global claims satisfy tenant operations only through explicitly named combined policies; ownership moves atomically with the immutable Owner role and context invalidation.

## Residual risks

- A compromised same-origin frontend can use in-memory access tokens and issue browser requests while controlled.
- Process-local rate limiting is not globally consistent across multiple instances.
- Argon2id computation cannot be forcibly interrupted inside the primitive after it begins.
- SQLite is unsuitable for high write concurrency or uncoordinated network-filesystem sharing.
- A compromised host process, secret store, database administrator boundary, or signing key can bypass application controls.

SharpAccess supports explicit `kid`-based signing-key rotation with controlled verification overlap; the obsolete claim that HMAC rotation lacks key-identifier overlap is not part of the current threat model.
