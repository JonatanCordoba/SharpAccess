# Security design

Vulnerability reporting, supported-version policy, and coordinated disclosure are defined in the repository-level [`SECURITY.md`](../SECURITY.md). This document describes the technical security model and host responsibilities.

## Password hashing, pepper, and password risk checks

Passwords are length-bounded before hashing and processed with Argon2id using a unique random salt. Encoded hashes include algorithm parameters and the pepper-version identifier. A successful login upgrades an account when its Argon2 parameters or pepper version are no longer current. Pepper values are external secrets and are never stored with the user record.

The default password policy requires a minimum length of 15 characters, a maximum length bound, at least one letter, and at least one digit. Hosts can replace `IPasswordRiskValidator` to reject common, leaked, account-derived, or policy-prohibited candidates before memory-hard hashing. The built-in validator rejects common sample-grade candidates and candidates containing the local email identifier.

Unknown-account login performs an equivalent Argon2id verification against a lazily created dummy hash to reduce email-enumeration timing differences. Failed-attempt increments and lockout transitions are atomic. Per-IP rate limiting and per-account lockout are separate controls.

## Email ownership and password recovery

Password accounts start unverified. Email/password login requires a verified, active account but returns the same generic failure used for other invalid credential states. Verification and reset tokens are cryptographically random, HMAC-hashed at rest, expiring, single-use, and purpose-bound. Password changes and resets increment the security version and revoke every refresh family.

## JWT access tokens and fresh authentication

Access tokens use the configured rotatable signing-key ring. The built-in configured ring supports HS256 with at least 256 bits; host-owned rings may use RS256, ES256, valid X.509-backed credentials, HSMs, or approved managed-key systems. Multi-service deployments should prefer asymmetric signing so verification services do not possess signing authority. SharpAccess validates the key type, algorithm, strength, lifetime, active private material, `kid`, activation, and retirement boundaries.

Tokens include issuer, audience, `sub`, `jti`, `iat`, `exp`, email, security and authorization versions, explicit global roles and permissions, and optional active-tenant roles, permissions, and owner state. Validation restricts the accepted algorithm, resolves exactly one accepted key by `kid`, and rechecks persisted account status, verification, versions, and tenant membership on every request. The package uses only the `SharpAccess.Jwt` bearer scheme and does not replace the host default.

Sensitive mutations require a recently issued access token. The default fresh-authentication window is 10 minutes and applies to password changes, explicit session revocation, administration mutations, and tenant mutations. Hosts can adjust `FreshAuthenticationMinutes` but it cannot exceed the configured access-token lifetime.

## Refresh tokens and browser request confirmation

Refresh tokens are opaque random values; only keyed hashes are stored. Rotation is transactional: the presented token is revoked and its replacement is created together. Presenting a previously rotated token revokes all active tokens in that family. Password, account-status, role, permission, tenant-role, and administrator-reseed changes invalidate access tokens through security-version rotation and revoke refresh sessions.

The default browser transport is an `HttpOnly`, `SameSite=Lax`, `Secure` cookie scoped to the configured authentication path. JSON refresh-token responses require explicit opt-in. The sample keeps access tokens only in JavaScript memory.

For non-loopback hosts, startup validation requires secure refresh cookies, a `__Secure-` cookie-name prefix, and explicit browser request confirmation for cookie-backed refresh and logout requests. The confirmation header defaults to `X-SharpAccess-CSRF: 1`. The package default refresh cookie is `sharpaccess_refresh`; production hosts should configure a `__Secure-` prefixed cookie name. Pre-v1 cookie and header names are not accepted. Trusted non-browser clients may continue to use explicit JSON refresh-token transport when enabled.

## Rate limiting and lockout

Login, registration, refresh, password-recovery, verification, and OpenID Connect endpoints use configurable fixed-window per-IP limits. Their privacy-preserving partitions require a dedicated HMAC key of at least 32 bytes whenever any corresponding feature is enabled; signing, token-hashing, pepper, and provider-client secrets are never fallback partition keys. Login additionally enforces configurable account lockout. Multi-instance deployments should replace process-local throttling with an appropriate shared implementation at the host or edge.

## OpenID Connect security

Each enabled keyed provider uses Authorization Code + PKCE `S256`, expiring single-use state, an OpenID Connect nonce, exact literal redirect URI matching, strict issuer/audience/signature/lifetime/issued-at/algorithm checks, verified email, and a bounded provider subject. Callback paths cannot contain route syntax or shadow a mapped SharpAccess route. Authorization, token, and JWKS endpoints must use HTTPS and remain inside the provider's explicit host allowlist; automatic HTTP redirects are disabled. Provider access and refresh tokens are not persisted. The callback returns only a short-lived, one-time local exchange code in the URL fragment.

An external identity can link to an existing account only when the provider email is verified and the local account is already active and verified. OpenID Connect success and failure events are audited without raw codes or provider tokens. The default configuration contains one disabled Google-compatible entry; Google is an example provider, not a provider-specific public contract.

## Authorization, tenants, and audit logs

Roles and permissions are persisted and resolved when sessions are issued. Tenant-scoped roles are included only for a current member. Tenant mutation endpoints require the token tenant to equal the route tenant, while JWT validation rechecks membership.

Security events include bounded actor, tenant, IP address, User-Agent, detail, and timestamp fields. Audit data must not contain passwords, raw tokens, authorization codes, secrets, or full exception details. Account, password, session, OAuth, role, permission, user-status, tenant, and tenant-role changes write audit records.

Mandatory security mutations commit their single canonical audit row in the same provider transaction. If the audit insert fails, the provider rolls back the state change and propagates the failure; a retry uses a fresh audit identifier. Core prepares bounded evidence before the mutation, while providers may enrich only identifiers established from trusted persisted state. Services do not append a second canonical row after commit.

Login results, recovery and verification requests, and OpenID Connect results are standalone observations rather than canonical mutation evidence. Their closed event set is `login_success`, `login_failed`, `password_reset_requested`, `email_verification_requested`, `oauth_login_success`, and `oauth_login_failed`. A storage failure on this explicit best-effort path increments the tag-free `sharpaccess.audit.observation_failures` counter and does not replace an already-determined response with an error; caller cancellation still propagates. This policy cannot be used for password, account-status, session invalidation, refresh replay, authorization, administrator-reseed, external-account-binding, or tenant mutation evidence. See [ADR 0013](adr/0013-atomic-security-audit-evidence.md).

## Diagnostic telemetry

Core authentication operations emit activities and metrics through the `SharpAccess` activity source and meter. The package emits only bounded operation, outcome, and error-type tags. It does not emit account identifiers, tenant identifiers, IP addresses, User-Agent values, raw tokens, result codes, exception messages, SQL, or connection strings.

Hosts must preserve this boundary when configuring exporters or enriching signals. Telemetry is operational data and needs access control, retention, and privacy review. See `docs/OBSERVABILITY.md` and `docs/PRIVACY.md`.

## Error handling and security headers

Expected failures map to bounded codes and appropriate statuses. Unexpected exceptions are logged server-side and returned as generic `ProblemDetails`; stack traces, exception types, SQL, filesystem paths, secrets, and token values are not returned. Request-aborted exception handling avoids writing through a canceled token.

`UseSharpAccess` does not select a host-wide exception policy or content security policy. Hosts can opt into `UseSharpAccessExceptionHandling` and `UseSharpAccessSecurityHeaders`; configured security headers preserve values already selected by the host, and CSP is omitted unless explicitly configured.

## Secret handling, Data Protection, and host responsibilities

Use independent random material for access-token signing, opaque-token HMAC, rate-limit partitions, password peppers, OpenID Connect client credentials, cookie confirmation values, and Data Protection protection. Keep private signing keys and symmetric secrets in an approved secret store, certificate store, HSM, or managed-key system. Verification-only services should receive public key material rather than signing authority where asymmetric signing is used. Keep prior pepper and verification-key versions only for their controlled overlap windows. Store secrets and Data Protection keys outside source control and ephemeral filesystems.

The sample production host requires a persistent Data Protection key directory and a certificate used to protect keys at rest. It also configures forwarded-header processing, HSTS, and HTTPS redirection in Production. The host controls trusted proxy configuration, HTTPS, HSTS, CORS allowlists, CSP additions, centralized logging, backups, distributed throttling, SMTP/provider security, monitoring, privacy obligations, and incident response.
