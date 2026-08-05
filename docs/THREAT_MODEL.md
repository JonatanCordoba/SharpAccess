# Threat model

## Protected assets

- Password verifiers and historical/current peppers.
- JWT signing, opaque-token and rate-limit HMAC, OpenID Connect client, and Data Protection keys.
- Verification, reset, OAuth state/code, access, and refresh tokens.
- User status, global authorization, tenant authorization, tenant ownership, memberships, OAuth links, and audit history.

## Trust boundaries

1. Browser or API client to the ASP.NET Core host.
2. Reverse proxy or load balancer to Kestrel.
3. Host process to the database provider.
4. Host process to email infrastructure.
5. Host process and browser redirects to configured OpenID Connect authorization/token/JWKS endpoints.
6. Deployment configuration and secret manager to the running process.
7. Global authorization catalog to each tenant-owned authorization catalog.

Data crossing a boundary is treated as untrusted: bodies, route values, headers, cookies, claims, OAuth payloads, environment values, and database rows are validated or bounded before use.

## Major threats and mitigations

| Threat | Principal mitigations |
|---|---|
| Credential stuffing and guessing | per-IP limits, atomic account lockout, Argon2id, generic failures |
| Email enumeration | generic registration, resend, forgot-password, and login behavior; dummy hash |
| Database disclosure | salted Argon2id, external pepper, keyed token hashes, no provider token persistence |
| Refresh theft and replay | secure cookie default, rotation, family reuse detection, metadata, security version |
| OAuth interception and CSRF | authorization code, PKCE S256, one-time state, nonce, exact redirect URI |
| Account-link takeover | verified provider email plus active, already-verified local account requirement |
| Stale authorization | short JWT lifetime plus persisted status, authorization-version, security-version, and tenant-membership checks |
| Cross-tenant route reuse | active tenant claim, persisted membership check, route/claim equality, tenant-keyed role and permission joins |
| Tenant-to-global privilege escalation | separate global and tenant catalogs, distinct JWT claim types, global-only `/admin/*` policies, provider contracts proving no cross-scope role or permission assignment |
| Global-to-tenant authority confusion | global permissions do not become tenant permissions; cross-tenant authority is accepted only by dedicated policies that explicitly name both accepted scopes |
| Ownership takeover or orphaning | unique persisted owner record, immutable tenant `Owner` role, existing-member requirement, locked atomic transfer, old/new context invalidation, one canonical audit row in the transfer transaction |
| Owner-role stripping | normal tenant-role assignment and removal paths reject the `Owner` role identifier; ownership changes only through the transfer operation |
| SQL injection | parameterized commands and bounded validated identifiers |
| XSS-assisted token theft | refresh token is HttpOnly; sample access token remains memory-only; host CSP required |
| Sensitive error disclosure | centralized sanitized `ProblemDetails` and bounded audit/log data |
| Concurrent state races | transactional refresh operations, serialized migrations, atomic lockout and ownership updates |
| Audit gap or duplicate canonical evidence | caller-created bounded evidence, transaction-local provider inserts, fail-closed rollback, one outcome-selected refresh row, no post-commit service append |

## Authorization invariants

- Global roles grant only global permissions.
- Tenant roles grant only tenant permissions within one active tenant.
- Tenant claims never satisfy a global permission policy.
- Global claims never satisfy a tenant permission policy unless a dedicated global-or-tenant policy explicitly accepts the named global permission.
- A tenant owner has one persisted owner record and the immutable tenant `Owner` role.
- The `Owner` role cannot be assigned or removed through ordinary tenant-role endpoints.
- Every ownership transfer moves the owner record and `Owner` role in one transaction and invalidates both affected authorization contexts.

## Assumptions

The host terminates HTTPS correctly, restricts trusted proxies, secures secrets and Data Protection keys, protects database and backups, registers a trustworthy email sender, uses supported dependencies, and monitors audit events. Configured provider endpoints and signing metadata are reached over authenticated TLS and remain inside explicit host allowlists.

## Residual risks

- A compromised same-origin frontend can use the in-memory access token and issue requests with the refresh cookie while the page remains controlled.
- Process-local rate limiting is not globally consistent across multiple instances.
- Argon2id computation cannot be forcibly interrupted inside the third-party primitive after it begins.
- HMAC signing-key rotation does not provide overlap through key identifiers in the current release.
- SQLite is unsuitable for high write concurrency or uncoordinated network-filesystem sharing.
- An attacker controlling the host process, secret store, database administrator boundary, or signing keys can bypass application-level controls.
