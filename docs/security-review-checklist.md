# Security review checklist

Use this checklist for pull requests that touch authentication, authorization, token handling, provider persistence, or package release behavior.

## Inputs and responses

- External input is validated before use.
- Public errors remain generic where account or token existence could be inferred.
- ProblemDetails responses do not include exception details, source paths, request bodies, raw tokens, passwords, OAuth codes, peppers, or signing keys.
- Request body logging is not introduced for auth endpoints.

## Password flows

- Password length limits are enforced before expensive hashing work where appropriate.
- Unknown-account paths perform comparable dummy hash verification.
- Password-risk validation remains outside the core password-flow implementation.
- Password reset increments security version and revokes active sessions.
- Password change verifies the current password and revokes active sessions.

## Token flows

- Access tokens validate issuer, audience, lifetime, signing key, algorithm, persisted user state, security version, and tenant membership.
- Refresh tokens remain opaque to clients.
- Refresh-token hashes, not raw refresh tokens, are persisted.
- Refresh tokens rotate on successful use.
- Reuse detection revokes the refresh-token family.
- Logout and explicit revocation do not leak whether a token exists unless the caller is authorized to know.
- One-time tokens are purpose-scoped, hashed at rest, expiring, and single-use.

## Tenant and authorization flows

- Tenant context is explicit.
- Tenant membership is checked on login, refresh, current-user load, and JWT validation for tenant-scoped tokens.
- Role and permission changes invalidate affected sessions.
- Endpoint handlers do not contain business authorization logic beyond attributes or service delegation.

## Provider persistence

- SQL uses parameters.
- Security-sensitive multi-row changes are transactional.
- Migrations are ordered and idempotent.
- Provider-specific SQL does not enter the core package.
- Provider-contract tests cover behavior, not just schema shape.

## Logging and audit

- Audit records contain event names and identifiers, not raw secrets.
- New security-sensitive actions add audit events.
- Audit writes do not block secure failure behavior from returning sanitized responses.
- Logs never include raw tokens, passwords, OAuth codes, peppers, signing keys, or request bodies for auth endpoints.

## Package and CI

- Public-surface tests are updated when public APIs intentionally change.
- Package-consumer validation is run before release candidates.
- Dependency audit, package validation, SBOM generation, and coverage scripts pass on Linux and Windows.
- Documentation is updated for new production controls or operator actions.
