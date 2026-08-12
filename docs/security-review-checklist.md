# Security review checklist

Use this checklist for changes touching authentication, authorization, tokens, provider persistence, repository controls, or package/release behavior.

## Inputs and responses

- External input is validated and bounded before use.
- Public errors remain generic where account/token existence could be inferred.
- ProblemDetails/logs do not expose exception details, source paths, request bodies, raw tokens, passwords, OAuth codes, peppers, signing keys, connection strings, or secrets.

## Password and token flows

- Password length/cost bounds are enforced before expensive work where applicable.
- Unknown-account login performs comparable dummy verification.
- Password change/reset increments security version and revokes active sessions.
- Access tokens validate issuer, audience, lifetime, algorithm/key, persisted account state, versions, and tenant context.
- Refresh/one-time tokens remain opaque, hashed at rest, expiring/purpose-bound, rotated/single-use as applicable.
- Refresh replay revokes the family.

## Tenant and authorization flows

- Global and tenant scopes remain distinct.
- Tenant membership/route context is checked on applicable operations.
- Security-sensitive authorization mutations invalidate affected sessions/contexts.
- Tenant ownership changes remain atomic and auditable.

## Provider persistence

- SQL is parameterized.
- Security-sensitive multi-row changes are transactional.
- Migrations are ordered/immutable and provider-owned.
- Provider-specific SQL does not enter Core.
- Provider-contract tests cover observable behavior.

## Package and CI

- Public API baselines are updated only for intentional reviewed changes.
- Package-consumer validation covers Core, SQLite, and PostgreSQL as the active cohort.
- Dependency review, DevSkim, tracked-secret scanning, Windows CI, provider checks, package validation, SBOMs, and coverage run as required by scope.
- No Linux/macOS/Bash/Docker/Compose/service-container parity is introduced.
- Documentation is updated for new production/operator controls.

RC1 is already published; post-release documentation-only changes do not recapture immutable RC1 protected release evidence.
