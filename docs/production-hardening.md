# Production hardening guide

This package is designed to be secure by default, but the host application and deployment environment still own several production controls. Treat this document as a release checklist before exposing SharpAccess endpoints to real users.

## Transport and proxy controls

- Serve every public endpoint over HTTPS.
- Configure trusted reverse-proxy headers before authentication middleware if the application runs behind a proxy or load balancer.
- Reject insecure public `BaseUri` values. Loopback HTTP is acceptable only for local development and tests.
- Keep `UseSharpAccess()` after proxy/header middleware and before endpoint authorization. Select package exception handling and security headers explicitly when the host does not provide equivalents.
- Keep refresh-token cookies scoped to the `/auth` path unless the host has a narrow reason to widen the path.

## Secrets and key material

Store every secret outside source control and deployment artifacts.

Required production secrets include:

- Rotatable JWT signing-key ring with an explicitly active key.
- Rotatable token-hashing key ring for opaque refresh, reset, verification, OpenID Connect state, and exchange tokens.
- Dedicated rate-limit partition key that is not reused from any other purpose.
- Active password pepper value and version.
- SMTP credentials if email flows are enabled.
- OpenID Connect client secrets if external login is enabled.
- Bootstrap administrator password only for explicit Development or Test seeding.

Minimum operational expectations:

- Use a managed secret store or equivalent OS-level secret injection.
- Rotate secrets through a documented change process.
- Do not log, trace, display, or serialize raw tokens, passwords, OAuth codes, peppers, or signing keys.
- Keep old password pepper versions only as long as needed for gradual hash upgrades.

## JWT signing-key rotation

SharpAccess issues with the configured active signing key and validates every retained, time-valid key by `kid`. Rotation can therefore preserve existing access tokens for their bounded remaining lifetime.

Recommended process:

1. Add the new key with a unique `kid` and a valid activation window while retaining the prior validation key.
2. Select the new key as active and deploy the complete ring consistently to every instance.
3. Monitor authentication failures, unknown-key events, and support channels.
4. Retire and remove the prior key only after its final issued access token plus allowed clock skew can no longer validate.

Refresh tokens do not become valid just because a JWT remains valid. Every authenticated request validates persisted account state, security version, and tenant membership.

## Token hashing-key rotation

The token-hashing ring protects lookup values for opaque tokens. New values use the active key version, while retained prior keys allow existing bounded-lifetime values to remain discoverable during a controlled rotation.

Recommended emergency process:

1. Add a new key version, select it as active, and deploy the complete ring consistently.
2. Retain prior key versions only until all values they protect have expired or been revoked.
3. For emergency compromise, remove the affected key, revoke refresh families, and invalidate outstanding reset, verification, OpenID Connect state, and exchange tokens.
4. Force users through login or reset flows as appropriate and record the event in operational incident notes.

## Password pepper rotation

Password pepper rotation should prefer gradual rehashing over forced resets when possible.

Recommended process:

1. Add the new pepper with a new version.
2. Mark the new version as active.
3. Keep previous pepper versions available for verification only.
4. Let successful password login or password change rehash credentials with the active pepper.
5. Remove retired pepper versions only after the migration window is complete.

If a pepper is suspected compromised, force password resets and revoke active refresh sessions.

## Database controls

- Run the auth database with least-privilege credentials.
- Back up the auth database and test restoration before production launch.
- Ensure backups receive the same secrecy controls as production data.
- Keep provider migrations under change control.
- Monitor migration failures and do not start the application after a failed auth migration.
- For SQLite deployments, ensure the database path is not under a static-file root and is writable only by the application identity.

## Email flows

Registration, verification, and reset flows require a reliable email sender.

- Do not use the development/test email sender in production.
- Ensure reset and verification links use the correct public `BaseUri`.
- Keep email content generic enough to avoid leaking account existence.
- Monitor email delivery failures.
- Rate-limit verification and password-reset requests.

## OpenID Connect controls

- Use exact redirect URI matching at the provider and in application configuration.
- Use HTTPS redirect URIs in production.
- Keep OpenID Connect client secrets in secret storage.
- Rotate client secrets through the provider console and host configuration together.
- Monitor OpenID Connect callback errors and state/nonce validation failures.

## Rate limiting and lockout

Tune rate limits for the host's traffic pattern before launch.

Recommended minimums:

- Keep login, registration, refresh, reset, verification, and OpenID Connect policies enabled.
- Configure an independent random rate-limit partition key; never reuse signing, token-hashing, pepper, or provider credentials.
- Use reverse-proxy-aware IP handling so rate limits partition by the real client address.
- Treat high lockout rates as either attack telemetry or UX friction.
- Keep lockout responses generic.

## Logging and audit

Application logs and audit records are different controls.

- Logs are for operators and diagnostics.
- Audit records are security events and should be retained according to policy.
- Never log raw tokens, passwords, OAuth codes, peppers, or signing keys.
- Avoid writing request bodies for auth endpoints.
- Preserve audit events for login failure, login success, reset requested/completed, verification requested/completed, external-account binding, refresh rotation, refresh reuse, family revocation, role changes, tenant changes, and administrator seeding.

## CORS and browser controls

- Allow only known origins in production.
- Do not use wildcard CORS with credentialed browser flows.
- Keep refresh-token cookies `HttpOnly`.
- Use `SameSite=Lax` or stricter unless a cross-site flow explicitly requires otherwise.
- Keep security headers enabled unless the host has a documented replacement.

## Administrator seeding

- Use administrator seeding only in Development or Test; the package rejects it in Production.
- Never commit seed passwords.
- Rotate the seed administrator password immediately if it was shared through a manual channel.
- Disable repeated seeding after the initial bootstrap unless there is an emergency recovery procedure.

## Operational readiness checklist

Before production launch:

- Build, tests, coverage, package, SBOM, and API-contract scripts pass on Linux and Windows.
- Dependency audit has no unresolved vulnerabilities at the configured severity.
- Secrets are injected through production secret storage.
- Public `BaseUri` is HTTPS and correct.
- Email sender is production-grade and verified.
- OpenID Connect redirect URIs are exact and HTTPS.
- Database backups and restores are tested.
- Audit retention and log redaction are configured.
- Incident response includes token revocation, key rotation, pepper compromise, and database restore procedures.
